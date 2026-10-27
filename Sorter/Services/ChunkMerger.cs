using Sorter.Entities;
using System.Text;

namespace Sorter.Services
{
    public static class ChunkMerger
    {
        private const int MB = 1024 * 1024;
        public const int DefaultMergeMemoryMb = 3 * 1024;                      // 3 GB for all chunk read buffers together
        public const long DefaultMergeMemoryBytes = (long)DefaultMergeMemoryMb * MB;
        private const int ReadBufferSizeMinBytes = 64 * 1024;         // floor: keeps reads from getting tiny with very many files
        private const int ReadBufferSizeMaxBytes = 4 * MB;            // cap: gains flatten out above a few MB

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Splits the memory budget evenly across the chunk readers, kept between the floor and the cap
        public static int GetReadBufferSize(int fileCount, long totalMemoryBytes = DefaultMergeMemoryBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(fileCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalMemoryBytes);

            long perFile = totalMemoryBytes / Math.Max(1, fileCount); // 0 files: avoid dividing by zero
            return (int)Math.Clamp(perFile, ReadBufferSizeMinBytes, ReadBufferSizeMaxBytes);
        }

        public static void Merge(IReadOnlyList<string> chunkFiles, string outputPath, string newLine,
            long totalMemoryBytes = DefaultMergeMemoryBytes)
        {
            if (newLine is not ("\n" or "\r\n"))
                throw new ArgumentException(@"New line must be ""\n"" or ""\r\n"".", nameof(newLine));

            int readBufferSize = GetReadBufferSize(chunkFiles.Count, totalMemoryBytes);

            List<StreamReader> readers = new List<StreamReader>(chunkFiles.Count);
            StreamWriter? writer = null;
            PriorityQueue<int, Entry> entryQueue = new PriorityQueue<int, Entry>();
            try
            {

                foreach (string file in chunkFiles)
                {
                    StreamReader sr = new StreamReader(file, new FileStreamOptions()
                    {
                        BufferSize = readBufferSize,
                        Options = FileOptions.SequentialScan,
                    });
                    readers.Add(sr);
                }
                writer = new StreamWriter(
                    outputPath,
                    Utf8NoBom,
                    new FileStreamOptions
                    {
                        Mode = FileMode.Create,
                        Access = FileAccess.Write,
                        BufferSize = 1024 * 1024        // FileStream buffer: 1 MB writes to disk
                    })
                { NewLine = newLine };

                //ix will contain index of reader that Entry belongs to
                foreach (var (ix, reader) in readers.Index())
                {
                    var line = reader.ReadLine();
                    if (line == null) reader.Close();
                    else entryQueue.Enqueue(ix, new Entry(line));

                }
                while (entryQueue.TryPeek(out int index, out var entry))
                {
                    writer.WriteLine(entry.Line);
                    var nextLine = readers[index].ReadLine();
                    if (nextLine != null)
                    {
                        entryQueue.DequeueEnqueue(index, new Entry(nextLine));
                    }
                    else
                    {
                        readers[index].Close();
                        entryQueue.Dequeue();
                    }
                }
            }
            finally
            {
                foreach (var sr in readers) sr.Dispose();
                writer?.Dispose();
            }
        }
    }

}
