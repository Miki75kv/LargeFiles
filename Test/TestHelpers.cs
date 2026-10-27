using Sorter.Entities;

namespace Test
{
    internal static class TestHelpers
    {
        public static void AssertSorted(string[] lines)
        {
            for (int i = 1; i < lines.Length; i++)
                Assert.True(new Entry(lines[i - 1]).CompareTo(new Entry(lines[i])) <= 0,
                    $"Not sorted at line {i + 1}: \"{lines[i - 1]}\" > \"{lines[i]}\"");
        }
    }
}
