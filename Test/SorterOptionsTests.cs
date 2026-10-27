using Sorter;
using Sorter.Services;

namespace Test
{
    public class SorterOptionsTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _inputPath;

        public SorterOptionsTests()
        {
            _tempDir = Directory.CreateTempSubdirectory("sorteroptions-tests-").FullName;
            _inputPath = Path.Combine(_tempDir, "in.txt");
            File.WriteAllText(_inputPath, "1. A\n");
        }

        public void Dispose()
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        [Theory]
        [InlineData("")]                    // no input file
        [InlineData("a.txt b.txt")]         // two input files
        [InlineData("a.txt --unknown 1")]   // unknown option
        [InlineData("a.txt --workers")]     // missing value
        [InlineData("a.txt --workers x")]   // not a number
        [InlineData("--workers 2 a.txt")]   // option before <input-file>
        public void When_Arguments_Cannot_Be_Parsed_Should_Return_Usage(string commandLine)
        {
            string[] args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            Assert.False(SorterOptions.TryParse(args, out _, out string? error));
            Assert.Equal(SorterOptions.Usage, error);
        }

        [Theory]
        [InlineData("--workers 0", "workers")]
        [InlineData("--max-chunk-mchars 0", "max-chunk-mchars")]
        [InlineData("--max-chunk-mchars 1025", "max-chunk-mchars")]
        [InlineData("--merge-memory-mb 0", "merge-memory-mb")]
        public void When_Number_Is_Out_Of_Range_Should_Name_The_Option(string option, string expectedInError)
        {
            string[] args = [_inputPath, .. option.Split(' ')];

            Assert.False(SorterOptions.TryParse(args, out _, out string? error));
            Assert.Contains(expectedInError, error);
        }

        [Fact]
        public void When_Input_File_Is_Missing_Should_Fail()
        {
            string[] args = [Path.Combine(_tempDir, "missing.txt")];

            Assert.False(SorterOptions.TryParse(args, out _, out string? error));
            Assert.Contains("Input file not found", error);
        }

        [Fact]
        public void When_Output_Equals_Input_Should_Fail()
        {
            string[] args = [_inputPath, "--output-file", _inputPath];

            Assert.False(SorterOptions.TryParse(args, out _, out string? error));
            Assert.Contains("cannot be the same", error);
        }

        [Fact]
        public void When_Output_Is_A_Folder_Should_Fail()
        {
            string[] args = [_inputPath, "--output-file", _tempDir];

            Assert.False(SorterOptions.TryParse(args, out _, out string? error));
            Assert.Contains("must include a file name", error);
        }

        [Fact]
        public void When_Output_Folder_Is_Missing_Should_Fail()
        {
            string[] args = [_inputPath, "--output-file", Path.Combine(_tempDir, "missing", "out.txt")];

            Assert.False(SorterOptions.TryParse(args, out _, out string? error));
            Assert.Contains("Output folder does not exist", error);
        }

        [Fact]
        public void When_Temp_Folder_Is_Missing_Should_Fail()
        {
            string[] args = [_inputPath, "--temp-dir", Path.Combine(_tempDir, "missing")];

            Assert.False(SorterOptions.TryParse(args, out _, out string? error));
            Assert.Contains("Temp folder does not exist", error);
        }

        [Fact]
        public void When_Only_Input_Is_Given_Should_Apply_Defaults()
        {
            Assert.True(SorterOptions.TryParse([_inputPath], out SorterOptions? options, out _));

            Assert.Equal(_inputPath, options.InputPath);
            Assert.Equal(Path.Combine(_tempDir, "in-sorted.txt"), options.OutputPath);
            Assert.Null(options.TempRoot);
            Assert.Equal(Environment.ProcessorCount, options.Workers);
            Assert.Null(options.MaxChunkMChars);
            Assert.Equal(ChunkMerger.DefaultMergeMemoryBytes, options.MergeMemoryBytes);
            Assert.False(options.Verbose);
        }

        [Fact]
        public void When_All_Options_Are_Given_Should_Use_Them()
        {
            string outputPath = Path.Combine(_tempDir, "out.txt");
            string[] args = [_inputPath, "--output-file", outputPath, "--temp-dir", _tempDir,
                "--workers", "3", "--max-chunk-mchars", "50", "--merge-memory-mb", "512", "--verbose"];

            Assert.True(SorterOptions.TryParse(args, out SorterOptions? options, out _));

            Assert.Equal(outputPath, options.OutputPath);
            Assert.Equal(_tempDir, options.TempRoot);
            Assert.Equal(3, options.Workers);
            Assert.Equal(50, options.MaxChunkMChars);
            Assert.Equal(512L * 1024 * 1024, options.MergeMemoryBytes); // MB converted to bytes
            Assert.True(options.Verbose);
        }
    }
}
