using Sorter.Services;
using System.Text;
using static Test.TestHelpers;

namespace Test
{
    public class ChunkSorterTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _chunkDir;
        private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

        public ChunkSorterTests()
        {
            // xUnit creates a new instance for every test, so each test gets its own folder
            _tempDir = Directory.CreateTempSubdirectory("chunksorter-tests-").FullName;
            _chunkDir = Directory.CreateDirectory(Path.Combine(_tempDir, "chunks")).FullName;
        }

        public void Dispose()
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        private string WriteInput(string content, Encoding? encoding = null)
        {
            string path = Path.Combine(_tempDir, "input.txt");
            File.WriteAllText(path, content, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        [Fact]
        public async Task When_Lines_Exceed_MaxLines_Should_Split_Into_Chunks()
        {
            string input = WriteInput("3. C\n1. A\n5. B\n2. B\n4. A\n");

            List<string> chunks = await ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 2, maxChars: 1000, workers: 4);

            Assert.Equal(3, chunks.Count);
            Assert.Equal(new[] { "1. A", "3. C" }, File.ReadAllLines(chunks[0]));
            Assert.Equal(new[] { "2. B", "5. B" }, File.ReadAllLines(chunks[1]));
            Assert.Equal(new[] { "4. A" }, File.ReadAllLines(chunks[2]));
        }

        [Fact]
        public async Task When_Lines_Fill_Last_Chunk_Exactly_Should_Not_Create_Empty_Chunk()
        {
            string input = WriteInput(
                "1. A\n" +
                "5. B\n" +
                "2. B\n" +
                "4. A\n");

            List<string> chunks = await ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 2, maxChars: 1000, workers: 4);

            Assert.Equal(2, chunks.Count);
            Assert.Equal(new[] { "1. A", "5. B" }, File.ReadAllLines(chunks[0]));
            Assert.Equal(new[] { "4. A", "2. B" }, File.ReadAllLines(chunks[1]));
        }

        [Fact]
        public async Task When_Chars_Reach_MaxChars_Should_Split_Into_Chunks()
        {
            string input = WriteInput(
                            "1. A\n" +
                            "5. B\n" +
                            "2. B\n" +
                            "4. A\n"
                            );

            List<string> chunks = await ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 1000, maxChars: 12, workers: 4);

            Assert.Equal(2, chunks.Count);
            Assert.Equal(new[] { "1. A", "2. B", "5. B" }, File.ReadAllLines(chunks[0]));
            Assert.Equal(new[] { "4. A" }, File.ReadAllLines(chunks[1]));
        }

        [Fact]
        public async Task When_Line_Is_Longer_Than_MaxChars_Should_End_Chunk_After_It()
        {
            string input = WriteInput(
                            "1. A\n" +
                            "2. " + new string('x', 100) + '\n' +
                            "5. B\n" +
                            "2. A\n"
                            );

            List<string> chunks = await ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 1000, maxChars: 5, workers: 4);

            Assert.Equal(2, chunks.Count);
            Assert.Equal(new[] { "1. A", "2. " + new string('x', 100) }, File.ReadAllLines(chunks[0]));
            Assert.Equal(new[] { "2. A", "5. B" }, File.ReadAllLines(chunks[1]));
        }

        [Fact]
        public async Task When_Input_Is_Empty_Should_Create_No_Chunks()
        {
            string input = WriteInput(string.Empty);

            List<string> chunks = await ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 1000, maxChars: 5, workers: 4);

            Assert.Empty(chunks);
        }

        [Fact]
        public async Task When_Line_Is_Invalid_Should_Throw_With_Line_Content()
        {
            string input = WriteInput(
                            "1. A\n" +
                            "5.B\n"  //no space after "."
                            );

            var exception = await Assert.ThrowsAsync<FormatException>(() => ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 1000, maxChars: 12, workers: 4));
            Assert.Contains("5.B", exception.Message);
        }

        [Fact]
        public async Task When_Input_Has_Bom_And_CrLf_Should_Write_Chunks_With_Lf_And_No_Bom()
        {
            string input = WriteInput("2. B\r\n1. A\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Assert.True(File.ReadAllBytes(input).AsSpan().StartsWith(Utf8Bom)); // the setup really wrote a BOM

            List<string> chunks = await ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 1000, maxChars: 1000, workers: 4);

            byte[] bytes = File.ReadAllBytes(Assert.Single(chunks));
            Assert.False(bytes.AsSpan().StartsWith(Utf8Bom), "Chunk file must not start with a BOM");
            Assert.Equal("1. A\n2. B\n", Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public async Task When_Chunks_Are_Written_Should_Be_Sorted_And_Contain_All_Input_Lines()
        {
            // Fixed seed: the same "random" input on every run, so a failure can be reproduced
            var random = new Random(42);
            string[] texts = ["Apple", "apple", "Apple pie", "Banana is yellow", "Cherry is the best", "Éclair"];
            string[] lines = Enumerable.Range(0, 1000)
                .Select(_ => $"{random.Next(1, 100)}. {texts[random.Next(texts.Length)]}")
                .ToArray();
            string input = WriteInput(string.Join("\n", lines) + "\n");

            List<string> chunks = await ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 7, maxChars: 1000, workers: 4);

            Assert.Equal(143, chunks.Count); // 1000 / 7, rounded up
            foreach (string chunk in chunks)
                AssertSorted(File.ReadAllLines(chunk));

            // Nothing lost or duplicated: same lines, compared as sorted plain strings
            Assert.Equal(
                lines.Order(StringComparer.Ordinal),
                chunks.SelectMany(File.ReadAllLines).Order(StringComparer.Ordinal));
        }

        private const int MB = 1024 * 1024;
        private const int MillionChars = 1_000_000;

        [Theory]
        [InlineData(0L, 8, null, 1 * MillionChars)]            // empty input: still a valid limit (the floor)
        [InlineData(1024L, 8, null, 1 * MillionChars)]         // tiny input: the 1 million chars floor
        [InlineData(400L * MB, 8, null, 50 * MB)]              // even split across workers (file bytes estimate chars)
        [InlineData(1024L * MB, 8, null, 100 * MillionChars)]  // large input: the default cap of 100 million chars
        [InlineData(1024L * MB, 1, 1, 1 * MillionChars)]       // the user's cap
        public void When_Computing_Chunk_Limits_Should_Split_Input_Within_Floor_And_Cap(long fileSizeBytes, int workers, int? maxChunkMChars, int expectedMaxChars)
        {
            var (maxLines, maxChars) = ChunkSorter.GetChunkLimits(fileSizeBytes, workers, maxChunkMChars);

            Assert.Equal(expectedMaxChars, maxChars);
            Assert.Equal(expectedMaxChars / 30, maxLines); // line limit assumes ~30 chars per line
        }

        [Theory]
        [InlineData(-1L, 8, null)]
        [InlineData(1024L, 0, null)]
        [InlineData(1024L, 8, 0)]
        [InlineData(1024L, 8, 1025)]
        public void When_Chunk_Limit_Arguments_Are_Invalid_Should_Throw(long fileSizeBytes, int workers, int? maxChunkMChars)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ChunkSorter.GetChunkLimits(fileSizeBytes, workers, maxChunkMChars));
        }
    }
}
