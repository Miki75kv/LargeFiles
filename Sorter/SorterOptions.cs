using Sorter.Services;
using System.Diagnostics.CodeAnalysis;

namespace Sorter
{
    // Command-line options of the sorter: parsed, defaults applied and validated
    public sealed record SorterOptions(
        string InputPath,
        string OutputPath,
        string? TempRoot,
        int Workers,
        int? MaxChunkSizeMb,
        long MergeMemoryBytes,
        bool Verbose)
    {
        private const int MB = 1024 * 1024;

        public const string Usage =
            """
            Usage: Sorter <input-file> [options]
              <input-file>                file to sort, lines in the form "<number>. <text>" (UTF-8)
            Options:
              --output-file <path>        result file, overwritten if it exists; its folder must exist
                                          (default: <input-name>-sorted<ext> next to the input file)
              --temp-dir <folder>         existing folder for temporary chunk files (default: system temp folder)
              --workers <int>             number of sorting workers (default: CPU count)
              --max-chunk-size-mb <int>   max chunk size in MB of text, counted in characters
                                          (1 char = 1 byte for ASCII; default 100, max 1024)
              --merge-memory-mb <int>     total memory for the merge's read buffers, in MB (default 3072)
              --verbose                   also print read, sort and write times for every chunk
            """;

        // Returns false with the message to print when the arguments are invalid
        public static bool TryParse(string[] args, [NotNullWhen(true)] out SorterOptions? options, [NotNullWhen(false)] out string? error)
        {
            options = null;

            if (!TryReadArgs(args, out string inputPath, out string? outputPath, out string? tempRoot,
                    out int? workers, out int? maxChunkSizeMb, out int? mergeMemoryMb, out bool verbose))
            {
                error = Usage;
                return false;
            }

            string fullInputPath = Path.GetFullPath(inputPath);
            string fullOutputPath = Path.GetFullPath(outputPath ?? DefaultOutputPath(fullInputPath));

            error = Validate(fullInputPath, fullOutputPath, tempRoot, workers, maxChunkSizeMb, mergeMemoryMb);
            if (error is not null)
                return false;

            options = new SorterOptions(
                fullInputPath,
                fullOutputPath,
                tempRoot,
                workers ?? Environment.ProcessorCount,
                maxChunkSizeMb,
                (long)(mergeMemoryMb ?? ChunkMerger.DefaultMergeMemoryMb) * MB, // MB from the user or the default, converted once
                verbose);
            return true;
        }

        private static string DefaultOutputPath(string inputPath) =>
            Path.Combine(
                Path.GetDirectoryName(inputPath) ?? "",
                $"{Path.GetFileNameWithoutExtension(inputPath)}-sorted{Path.GetExtension(inputPath)}");

        // Returns null when valid. Checks on the numbers first, then the ones that touch the disk.
        private static string? Validate(string inputPath, string outputPath, string? tempRoot,
            int? workers, int? maxChunkSizeMb, int? mergeMemoryMb)
        {
            if (workers <= 0)
                return "Param workers must be a positive int";
            if (maxChunkSizeMb is <= 0 or > ChunkSorter.MaxChunkSizeMbLimit)
                return $"Param max-chunk-size-mb must be between 1 and {ChunkSorter.MaxChunkSizeMbLimit}";
            if (mergeMemoryMb <= 0)
                return "Param merge-memory-mb must be a positive int";

            if (!File.Exists(inputPath))
                return $"Input file not found: {inputPath}";
            if (string.Equals(outputPath, inputPath, StringComparison.OrdinalIgnoreCase))
                return $"Output file path and input file path cannot be the same: {outputPath}";
            if (Path.EndsInDirectorySeparator(outputPath) || Directory.Exists(outputPath))
                return $"Output file path must include a file name, not just a folder: {outputPath}";
            string? outputFolder = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(outputFolder))
                return $"Output folder does not exist: {outputFolder}";
            // Only the per-run sorter-<guid> subfolder is created (and deleted) inside it
            if (tempRoot is not null && !Directory.Exists(tempRoot))
                return $"Temp folder does not exist: {tempRoot}";

            return null;
        }

        private static bool TryReadArgs(string[] args, out string inputFilePath, out string? outputFilePath, out string? tempRoot,
            out int? workerCount, out int? maxChunkSizeMB, out int? mergeMemoryMB, out bool verbose)
        {
            inputFilePath = "";
            outputFilePath = null;
            tempRoot = null;
            workerCount = null;
            maxChunkSizeMB = null;
            mergeMemoryMB = null;
            verbose = false;

            if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
                return false; // <input-file> must come first, before any option
            inputFilePath = args[0];

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--output-file")
                {
                    if (i + 1 >= args.Length)
                        return false;
                    outputFilePath = args[++i];
                }
                else if (args[i] == "--temp-dir")
                {
                    if (i + 1 >= args.Length)
                        return false;
                    tempRoot = args[++i];
                }
                else if (args[i] == "--workers")
                {
                    if (i + 1 >= args.Length)
                        return false;
                    if (!int.TryParse(args[++i], out int workers))
                        return false;
                    workerCount = workers;
                }
                else if (args[i] == "--max-chunk-size-mb")
                {
                    if (i + 1 >= args.Length)
                        return false;
                    if (!int.TryParse(args[++i], out int maxChunkSize))
                        return false;
                    maxChunkSizeMB = maxChunkSize;
                }
                else if (args[i] == "--merge-memory-mb")
                {
                    if (i + 1 >= args.Length)
                        return false;
                    if (!int.TryParse(args[++i], out int mergeMemory))
                        return false;
                    mergeMemoryMB = mergeMemory;
                }
                else if (args[i] == "--verbose")
                {
                    verbose = true; // a flag: takes no value
                }
                else
                    return false; // unknown option or an extra positional argument
            }

            return inputFilePath.Length > 0;
        }
    }
}
