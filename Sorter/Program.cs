using Sorter.Services;
using System.Diagnostics;

namespace Sorter
{
    internal class Program
    {
        private const int MB = 1024 * 1024;

        static async Task<int> Main(string[] args)
        {
            if (!SorterOptions.TryParse(args, out SorterOptions? options, out string? error))
            {
                Console.Error.WriteLine(error);
                return 2;
            }

            return await RunAsync(options);
        }

        static async Task<int> RunAsync(SorterOptions options)
        {
            var total = Stopwatch.StartNew();
            DirectoryInfo? tempDir = null;
            try
            {
                long fileSizeBytes = new FileInfo(options.InputPath).Length;
                var (maxLinesPerChunk, maxCharsPerChunk) = ChunkSorter.GetChunkLimits(fileSizeBytes, options.Workers, options.MaxChunkSizeMb);
                Console.WriteLine($"Sorting {options.InputPath} ({fileSizeBytes / MB:N0} MB), chunks up to {maxCharsPerChunk / MB:N0} MB of text...");

                // Unique subfolder per run, so cleanup is a single recursive delete
                tempDir = options.TempRoot is null
                    ? Directory.CreateTempSubdirectory("sorter-")
                    : Directory.CreateDirectory(Path.Combine(options.TempRoot, "sorter-" + Guid.NewGuid().ToString("N")));                

                //create sorted files from input
                var phase = Stopwatch.StartNew();
                string newLine = NewLineDetector.DetermineFirstLineEnding(options.InputPath);
                var files = await ChunkSorter.WriteSortedChunksAsync(options.InputPath, tempDir.FullName, maxLinesPerChunk, maxCharsPerChunk, options.Workers, options.Verbose);
                Console.WriteLine($"Split: {files.Count:N0} sorted chunks in {phase.Elapsed.TotalSeconds:F1} s");

                //merging sorted files into single file
                phase.Restart();
                ChunkMerger.Merge(files, options.OutputPath, newLine, options.MergeMemoryBytes);
                Console.WriteLine($"Merge: {files.Count:N0} chunks into {options.OutputPath} in {phase.Elapsed.TotalSeconds:F1} s");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error happened. {ex}");
                return 1;
            }
            finally
            {
                try
                {
                    tempDir?.Delete(true);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error happened on delete of temp folder {tempDir?.FullName}. Error: {ex}");
                }
            }

            // Total includes deleting the temp files; peak memory is the most the process held at any point
            using var process = Process.GetCurrentProcess();
            Console.WriteLine($"Done in {total.Elapsed.TotalSeconds:F1} s, peak memory {process.PeakWorkingSet64 / MB:N0} MB");

            // How hard the garbage collector worked: collections per generation, time the program was paused, bytes ever allocated
            TimeSpan gcPause = GC.GetTotalPauseDuration();
            Console.WriteLine($"GC: gen0 {GC.CollectionCount(0):N0}, gen1 {GC.CollectionCount(1):N0}, gen2 {GC.CollectionCount(2):N0} collections, " +
                $"paused {gcPause.TotalSeconds:F1} s ({gcPause / total.Elapsed:P0} of the run), allocated {GC.GetTotalAllocatedBytes() / MB:N0} MB in total");
            return 0;
        }
    }
}
