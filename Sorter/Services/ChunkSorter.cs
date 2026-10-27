using Sorter.Entities;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Sorter.Services
{
    public static class ChunkSorter
    {
        private const int MillionChars = 1_000_000;      // chunk sizes are given in millions of characters, not bytes
        public const int DefaultMaxChunkMChars = 100;
        public const int MaxChunkMCharsLimit = 1024;     // ~3.5 GB of heap per chunk; also keeps the char count within int
        private const int MinChunkChars = 1 * MillionChars; // tiny inputs: one small chunk instead of many near-empty ones
        private const int AssumedAvgLineChars = 30;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // The chunk size splits the input evenly across workers,
        // capped at maxChunkMChars (default 100 million chars) so memory stays bounded on large inputs.
        public static (int MaxLines, int MaxChars) GetChunkLimits(long fileSizeBytes, int workers, int? maxChunkMChars = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(fileSizeBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workers);
            if (maxChunkMChars is not null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkMChars.Value, nameof(maxChunkMChars));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(maxChunkMChars.Value, MaxChunkMCharsLimit, nameof(maxChunkMChars));
            }

            long capChars = (long)(maxChunkMChars ?? DefaultMaxChunkMChars) * MillionChars;

            // The chunk limits count chars (what the heap holds), but up front only the file size in bytes is known.
            // In UTF-8 every char takes at least 1 byte, so bytes are an upper estimate of chars (exact for ASCII).
            // A wrong estimate only makes the split across workers less even; memory is still bounded by chars.
            long estimatedChars = fileSizeBytes;
            long perWorkerChars = estimatedChars / workers;
            int maxChars = (int)Math.Clamp(perWorkerChars, MinChunkChars, capChars);

            // Bounds memory when lines are short (per-line overhead dominates) and sizes each list once
            int maxLines = maxChars / AssumedAvgLineChars;

            return (maxLines, maxChars);
        }

        // verbose: print read, parse, sort, write and wait times for every chunk (two lines per chunk), plus a busy/wait summary per worker
        public static async Task<List<string>> WriteSortedChunksAsync(string inputPath, string tempDirectory, int maxLines, int maxChars, int workers, bool verbose = false)
        {            
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLines);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workers);

            using var cts = new CancellationTokenSource();
            
            const int customBufferSize = 1024 * 1024;
            List<string> tempFiles = new List<string>();

            var freeLists = Channel.CreateUnbounded<List<string>>(new UnboundedChannelOptions { SingleReader = true });
            for (int i = 0; i < workers + 1; i++)
                freeLists.Writer.TryWrite(new List<string>(maxLines));    // TryWrite always succeeds on an unbounded channel

            // Queue: full lists waiting for a worker
            var fullLists = Channel.CreateUnbounded<(List<string> Lines, string TempFilePath, int ChunkNo)>(new UnboundedChannelOptions { SingleWriter = true });

            // Workers (consumers)
            var workerTasks = new Task[workers];
            for (int w = 0; w < workers; w++)
            {
                var workerNo = w;
                workerTasks[w] = Task.Run(async () =>
                {
                    try
                    {
                        var entries = new List<Entry>(maxLines);
                        var waitTimer = new Stopwatch();    // not started: the start-up wait for the first chunk counts as 0
                        double waitTotal = 0, busyTotal = 0;
                        // Ends by itself once the producer calls Complete() and the queue is empty
                        await foreach (var (lines, tempFilePath, chunkNo) in fullLists.Reader.ReadAllAsync(cts.Token))
                        {
                            var fullListWaiting = waitTimer.Elapsed.TotalSeconds;
                            waitTotal += fullListWaiting;
                            var itemsCount = lines.Count;
                            var parseTime = ParseChunk(lines, entries);
                            var (sortTime, writeTime) = SortAndFlush(entries, tempFilePath);
                            busyTotal += parseTime + sortTime + writeTime;
                            if (verbose)
                                Console.WriteLine($"Chunk {chunkNo}: {itemsCount} items on thread {Environment.CurrentManagedThreadId} with workerNo {workerNo}, parsing in {parseTime:F1} s, sorting in {sortTime:F1} s, writing in {writeTime:F1} s. Waiting on full list took {fullListWaiting:F1} s");
                            // Only after writing: returning it earlier would let the reader hold more chunks in memory
                            lines.Clear();
                            await freeLists.Writer.WriteAsync(lines, cts.Token);       // give the list back to the pool
                            waitTimer.Restart();    // from here until the next chunk arrives the worker is idle
                        }
                        if (verbose)
                            Console.WriteLine($"Worker {workerNo}: busy {busyTotal:F1} s, waiting on full lists {waitTotal:F1} s (start-up wait not counted)");
                    }
                    catch
                    {
                        cts.Cancel();   // wake the reader and stop the other workers
                        throw;          // keep the original exception and its stack trace
                    }

                });
            }


            // Producer
            Task readerTask = Task.Run(async () =>
            {

                try
                {
                    using StreamReader reader = new StreamReader(
                    inputPath,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    new FileStreamOptions
                    {
                        BufferSize = customBufferSize, // FileStream buffer: 1 MB reads from disk
                        Options = FileOptions.SequentialScan
                    });
                    int chunkNo = 0;
                    while (true)
                    {
                        var waitTimer = Stopwatch.StartNew();
                        List<string> list = await freeLists.Reader.ReadAsync(cts.Token);
                        var freeListWaiting = waitTimer.Elapsed.TotalSeconds;
                        var (itemsCount, chunkCreationTime) = FillChunk(reader, list, maxLines, maxChars);
                        if (itemsCount == 0) break;// end of file, nothing left to send
                        var tempFilePath = Path.Combine(tempDirectory, $"{++chunkNo}.txt");
                        if (verbose)
                            Console.WriteLine($"Produced chunk {chunkNo}: {itemsCount} items on thread {Environment.CurrentManagedThreadId} in {chunkCreationTime:F1} s. Waiting on free list took {freeListWaiting:F1} s");
                        await fullLists.Writer.WriteAsync((list, tempFilePath, chunkNo), cts.Token);
                        tempFiles.Add(tempFilePath);
                    }

                }
                catch
                {
                    cts.Cancel();
                    throw;
                }
                finally
                {
                    fullLists.Writer.Complete();    // no more batches: the workers' loops can end
                }
            });

            // waits for the reader and every worker; rethrows the real error, not the cancellation it caused
            await Task.WhenAll([readerTask, .. workerTasks]); 
            return tempFiles;
        }

        // Reads lines until the chunk is full or the file ends; 0 lines means the end of the file was already reached
        private static (int LineCount, double ReadSeconds) FillChunk(StreamReader reader, List<string> list, int maxLines, int maxChars)
        {
            var readTimer = Stopwatch.StartNew();
            var chunkChars = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                list.Add(line);
                chunkChars += line.Length;
                if (list.Count >= maxLines || chunkChars >= maxChars)
                {
                    break;
                }
            }
            return (list.Count, readTimer.Elapsed.TotalSeconds);
        }

        private static double ParseChunk(List<string> lines, List<Entry> entries)
        {
            var parseTimer = Stopwatch.StartNew();
            foreach (string line in lines)
                entries.Add(new Entry(line));
            return parseTimer.Elapsed.TotalSeconds;
        }

        private static (double SortSeconds, double WriteSeconds) SortAndFlush(List<Entry> items, string tempFilePath)
        {
            var chunkTimer = Stopwatch.StartNew();
            items.Sort();
            double sortTime = chunkTimer.Elapsed.TotalSeconds;
            chunkTimer.Restart();
            using (var writer = new StreamWriter(
                tempFilePath,
                Utf8NoBom,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,      // fail if the file already exists (would mean a naming bug)
                    Access = FileAccess.Write,
                    BufferSize = 1024 * 1024        // FileStream buffer: 1 MB writes to disk
                }))
            {
                writer.NewLine = "\n";          // temp files always use \n; only the final output uses the detected newline
                foreach (var entry in items)
                    writer.WriteLine(entry.Line);
            }
            double writeTime = chunkTimer.Elapsed.TotalSeconds;
            items.Clear();
            return (sortTime, writeTime);
        }
    }
}
