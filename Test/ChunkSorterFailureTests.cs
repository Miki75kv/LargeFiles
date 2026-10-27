using Sorter.Services;
using System.Text;

namespace Test
{
    // When the reader or a worker fails, the real error must come out and nothing may hang.
    // The timeout turns a deadlock into a failed test instead of a blocked test run.
    public class ChunkSorterFailureTests : IDisposable
    {
        private const int TimeoutMs = 10_000;

        private readonly string _tempDir;
        private readonly string _chunkDir;

        public ChunkSorterFailureTests()
        {
            _tempDir = Directory.CreateTempSubdirectory("chunksorter-failure-tests-").FullName;
            _chunkDir = Directory.CreateDirectory(Path.Combine(_tempDir, "chunks")).FullName;
        }

        public void Dispose()
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        private string WriteInput(string content)
        {
            string path = Path.Combine(_tempDir, "input.txt");
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        private static string ValidLines(int count) =>
            string.Concat(Enumerable.Range(1, count).Select(i => $"{i}. Text {i % 7}\n"));

        [Fact(Timeout = TimeoutMs)]
        public async Task When_Worker_Fails_Should_Throw_Its_Error_After_All_Tasks_Stop()
        {
            string input = WriteInput(ValidLines(20)); // 10 chunks of 2 lines
            File.WriteAllText(Path.Combine(_chunkDir, "2.txt"), "taken"); // the worker for chunk 2 can't create its file

            // A single worker: once it fails, its list never returns to the pool (2 lists), so without
            // cancellation the reader would wait for a free list forever
            // Exactly IOException: an OperationCanceledException here would mean the real error was lost
            await Assert.ThrowsAsync<IOException>(
                () => ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 2, maxChars: 1000, workers: 1));

            // On Windows a file that is still open can't be deleted: this fails if a worker was still writing
            Directory.Delete(_chunkDir, recursive: true);
        }

        [Fact(Timeout = TimeoutMs)]
        public async Task When_Reader_Fails_While_Workers_Are_Busy_Should_Throw_Its_Error()
        {
            string input = WriteInput(ValidLines(20) + "5.B\n"); // 10 chunks go to the workers before the invalid line

            var exception = await Assert.ThrowsAsync<FormatException>(
                () => ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 2, maxChars: 1000, workers: 4));

            Assert.Contains("5.B", exception.Message);
            Directory.Delete(_chunkDir, recursive: true);
        }

        [Fact(Timeout = TimeoutMs)]
        public async Task When_Input_Cannot_Be_Opened_Should_Throw_Instead_Of_Hanging()
        {
            string input = WriteInput(ValidLines(10));

            // Held open without sharing, as if another program had it: opening it for reading fails
            using (new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Assert.ThrowsAsync<IOException>(
                    () => ChunkSorter.WriteSortedChunksAsync(input, _chunkDir, maxLines: 2, maxChars: 1000, workers: 4));
            }
        }
    }
}
