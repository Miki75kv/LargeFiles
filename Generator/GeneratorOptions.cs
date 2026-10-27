using System.Diagnostics.CodeAnalysis;

namespace Generator
{
    public sealed record GeneratorOptions(
        string OutputPath,
        long FileSizeInBytes,
        int UniquePhraseCount,
        int Seed)
    {
        private const int MB = 1024 * 1024;

        public const string Usage =
            """
            Usage: Generator <output-file> <size-mb> [--unique <int>] [--seed <int>]
              <output-file>  path of the file to create (overwritten if it exists); its folder must exist
              <size-mb>      target file size in MB; the file ends at most one line short of it
            Options:
              --unique       number of distinct text parts to repeat across lines (default 1000, max 50000)
              --seed         seed for random generation; the same seed gives the same file
                             (default: random, printed at the end)
            """;

        // Returns false with the message to print when the arguments are invalid
        public static bool TryParse(string[] args, [NotNullWhen(true)] out GeneratorOptions? options, [NotNullWhen(false)] out string? error)
        {
            options = null;

            if (!TryReadArgs(args, out string outputPath, out int fileSizeMb, out int? uniquePhrases,
                    out int? seed))
            {
                error = Usage;
                return false;
            }

            string fullOutputPath = Path.GetFullPath(outputPath);

            error = Validate(fullOutputPath, fileSizeMb, uniquePhrases, seed);
            if (error is not null)
                return false;

            options = new GeneratorOptions(
                fullOutputPath,
                (long)fileSizeMb * MB,
                uniquePhrases ?? FileGenerator.DefaultUniquePhrasesCount,
                seed ?? Random.Shared.Next()); 
            return true;
        }

        private static string? Validate(string outputPath, int fileSizeMb, int? uniquePhrases, int? seed)
        {
            if (fileSizeMb <= 0)
                return "Param size-mb must be a positive int";
            if (uniquePhrases is <= 0 or > FileGenerator.MaxUniquePhrasesCount)
                return $"Param unique must be between 1 and {FileGenerator.MaxUniquePhrasesCount}";            

            if (Path.EndsInDirectorySeparator(outputPath) || Directory.Exists(outputPath))
                return $"Output file path must include a file name, not just a folder: {outputPath}";
            string? outputFolder = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(outputFolder))
                return $"Output folder does not exist: {outputFolder}";
            

            return null;
        }

        private static bool TryReadArgs(string[] args, out string outputFilePath, out int fileSizeMb, out int? uniqueCount,
            out int? seed)
        {
            outputFilePath = string.Empty;
            fileSizeMb = 0;
            uniqueCount = null;
            seed = null;

            if (args.Length < 2 || args[0].StartsWith("--", StringComparison.Ordinal) || args[1].StartsWith("--", StringComparison.Ordinal))
                return false;                                  // need <output-file> <size-mb> before any option

            outputFilePath = args[0];
            if (!int.TryParse(args[1], out fileSizeMb))
                return false;

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--unique")
                {
                    if (i + 1 >= args.Length)
                        return false;
                    if (!int.TryParse(args[++i], out int unique))
                        return false;
                    uniqueCount = unique;
                }
                else if (args[i] == "--seed")
                {
                    if (i + 1 >= args.Length)
                        return false;
                    if (!int.TryParse(args[++i], out int seedIn))
                        return false;
                    seed = seedIn;
                }
                else if (args[i].StartsWith("--", StringComparison.Ordinal))
                    return false; // unknown option                
                else
                    return false;//an unexpected extra argument
            }

            return outputFilePath.Length > 0;
        }
    }
}
