using Sorter.Services;
using System.Text;
using static Test.TestHelpers;

namespace Test
{
    public class ChunkMergerTests : IDisposable
    {
        private readonly string _tempDir;

        public ChunkMergerTests()
        {
            _tempDir = Directory.CreateTempSubdirectory("chunkMerger-tests-").FullName;
        }
        private string WriteInput(string fileName, string content, Encoding? encoding = null)
        {
            string path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, content, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        public void Dispose()
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void When_Chunk_Files_Provider_Should_Create_Sorted_File()
        {
            string inputFile1 = WriteInput("1.txt", "1. A\n5. B\n5. C\n");
            string inputFile2 = WriteInput("2.txt", "2. A\n3. B\n5. C\n");

            string resultFilePath = Path.Combine(_tempDir, "result.txt");
            ChunkMerger.Merge([inputFile1, inputFile2], resultFilePath, "\n");

            Assert.Equal(new[] { "1. A", "2. A", "3. B", "5. B", "5. C", "5. C" }, File.ReadAllLines(resultFilePath));
        }

        [Fact]
        public void When_Empty_Chunk_File_Provider_Should_Create_Sorted_File()
        {
            string inputFile1 = WriteInput("1.txt", "1. A\n5. B\n5. C\n");
            string inputFile2 = WriteInput("2.txt",string.Empty);

            string resultFilePath = Path.Combine(_tempDir, "result.txt");
            ChunkMerger.Merge([inputFile1, inputFile2], resultFilePath, "\n");

            Assert.Equal(new[] { "1. A",  "5. B", "5. C" }, File.ReadAllLines(resultFilePath));
        }

        [Fact]
        public void When_No_Chunk_Files_Should_Create_Empty_Output()
        {
            string resultFilePath = Path.Combine(_tempDir, "result.txt");

            ChunkMerger.Merge([], resultFilePath, "\n");

            Assert.True(File.Exists(resultFilePath));
            Assert.Equal(0, new FileInfo(resultFilePath).Length);
        }

        [Fact]
        public void When_Single_Chunk_File_Should_Copy_It()
        {
            string content = "1. A\n2. A\n1. B\n";
            string inputFile = WriteInput("1.txt", content);
            string resultFilePath = Path.Combine(_tempDir, "result.txt");

            ChunkMerger.Merge([inputFile], resultFilePath, "\n");

            Assert.Equal(content, File.ReadAllText(resultFilePath));
        }

        [Fact]
        public void When_NewLine_Is_CrLf_Should_End_Every_Line_With_CrLf()
        {
            string inputFile1 = WriteInput("1.txt", "1. A\n3. C\n");
            string inputFile2 = WriteInput("2.txt", "2. B\n");
            string resultFilePath = Path.Combine(_tempDir, "result.txt");

            ChunkMerger.Merge([inputFile1, inputFile2], resultFilePath, "\r\n");

            Assert.Equal("1. A\r\n2. B\r\n3. C\r\n", File.ReadAllText(resultFilePath));
        }

        [Fact]
        public void When_Merging_Should_Write_Output_Without_Bom()
        {
            string inputFile = WriteInput("1.txt", "1. A\n2. B\n");
            string resultFilePath = Path.Combine(_tempDir, "result.txt");

            ChunkMerger.Merge([inputFile], resultFilePath, "\n");

            byte[] bytes = File.ReadAllBytes(resultFilePath);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "Output must not start with a UTF-8 BOM");
        }

        [Fact]
        public void When_Output_Exists_Should_Overwrite_It()
        {
            string inputFile = WriteInput("1.txt", "1. A\n");
            // Longer than the merge result: a missing truncate would leave its tail behind
            string resultFilePath = WriteInput("result.txt", "old content that is much longer than the result\n");

            ChunkMerger.Merge([inputFile], resultFilePath, "\n");

            Assert.Equal("1. A\n", File.ReadAllText(resultFilePath));
        }

        [Theory]
        [InlineData("\r")]
        [InlineData("")]
        [InlineData("\\n")] // backslash + n, not a newline
        public void When_NewLine_Is_Invalid_Should_Throw(string newLine)
        {
            var exception = Assert.Throws<ArgumentException>(() => ChunkMerger.Merge([], "somepath", newLine));
            Assert.Contains("New line must", exception.Message);
        }

        [Fact]
        public void When_Merge_Fails_Should_Release_Chunk_Files()
        {
            string chunk1 = WriteInput("1.txt", "1. A\n3. C\n");
            string chunk2 = WriteInput("2.txt", "2. B\nnot a valid line\n");
            string resultFilePath = Path.Combine(_tempDir, "result.txt");

            Assert.Throws<FormatException>(
                () => ChunkMerger.Merge([chunk1, chunk2], resultFilePath, "\n"));

            // On Windows a file with an open handle can't be deleted:
            // each Delete throws IOException if Merge left a reader or the writer open
            File.Delete(chunk1);
            File.Delete(chunk2);
            File.Delete(resultFilePath);
        }

        [Fact]
        public async Task When_Sorting_And_Merging_Should_Produce_Sorted_Complete_Output()
        {
            var random = new Random(42);
            string[] texts = ["Apple", "apple", "Apple pie", "Banana is yellow", "Cherry is the best", "Éclair"];
            string[] lines = Enumerable.Range(0, 1000)
                .Select(_ => $"{random.Next(1, 100)}. {texts[random.Next(texts.Length)]}")
                .ToArray();
            string input = WriteInput("input.txt", string.Join("\n", lines) + "\n");
            string chunkDir = Directory.CreateDirectory(Path.Combine(_tempDir, "chunks")).FullName;

            List<string> chunks = await ChunkSorter.WriteSortedChunksAsync(input, chunkDir, maxLines: 7, maxChars: 1000, workers: 1);
            // The test is only meaningful if the merge has to interleave many files
            Assert.True(chunks.Count > 100, $"Expected many chunks to merge, got {chunks.Count}");

            string resultFilePath = Path.Combine(_tempDir, "result.txt");
            ChunkMerger.Merge(chunks, resultFilePath, "\n");

            var resultingLines = File.ReadAllLines(resultFilePath);
            AssertSorted(resultingLines);

            Assert.Equal(lines.Order(StringComparer.Ordinal), resultingLines.Order(StringComparer.Ordinal));
        }

        private const int MB = 1024 * 1024;

        [Theory]
        [InlineData(0, 1024L * MB, 4 * MB)]        // no files: no division by zero, capped
        [InlineData(1, 1024L * MB, 4 * MB)]        // one file: the cap
        [InlineData(1024, 1024L * MB, 1 * MB)]     // 1 GB / 1024 files
        [InlineData(100_000, 1024L * MB, 64 * 1024)] // very many files: the floor
        public void When_Budget_Is_Split_Across_Files_Should_Stay_Within_Floor_And_Cap(int fileCount, long totalMemoryBytes, int expectedBytes)
        {
            Assert.Equal(expectedBytes, ChunkMerger.GetReadBufferSize(fileCount, totalMemoryBytes));
        }

        [Fact]
        public void When_Budget_Is_Not_Given_Should_Use_3GB_Default()
        {
            Assert.Equal(3 * MB, ChunkMerger.GetReadBufferSize(1024)); // 3 GB / 1024 files
        }

        [Theory]
        [InlineData(-1, 1024L * MB)]
        [InlineData(1, 0L)]
        [InlineData(1, -1L)]
        public void When_Buffer_Size_Arguments_Are_Invalid_Should_Throw(int fileCount, long totalMemoryBytes)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ChunkMerger.GetReadBufferSize(fileCount, totalMemoryBytes));
        }
    }
}
