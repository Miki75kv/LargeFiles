using System.Diagnostics;

namespace Generator
{
    internal class Program
    {
        static int Main(string[] args)
        {
            if (!GeneratorOptions.TryParse(args, out GeneratorOptions? options, out string? error))
            {
                Console.Error.WriteLine(error);
                return 2;
            }

            return Run(options);
        }

        private static int Run(GeneratorOptions options)
        {
            try
            {
                // Printed before the work starts, so the seed is known even if the run fails halfway
                Console.WriteLine($"Generating {options.FileSizeInBytes / (1024 * 1024):N0} MB to {options.OutputPath} " +
                    $"(seed {options.Seed}, {options.UniquePhraseCount:N0} distinct phrases)...");

                var stopwatch = Stopwatch.StartNew();
                var (bytes, lines) = FileGenerator.Generate(options.OutputPath, options.FileSizeInBytes, options.UniquePhraseCount, options.Seed);
                stopwatch.Stop();

                // The seed is printed so this exact file can be generated again with --seed
                Console.WriteLine($"Generated {bytes:N0} bytes ({lines:N0} lines) to {options.OutputPath} in {stopwatch.Elapsed.TotalSeconds:F1} s (seed {options.Seed})");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error happened. {ex}");
                return 1;
            }
            return 0;
        }
    }
}
