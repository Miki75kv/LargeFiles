using Sorter.Entities;

namespace Test
{
    public class EntryTests
    {
        [Theory]
        [InlineData("123. Some text", 123, "Some text")]
        [InlineData("007. Some text", 7, "Some text")]
        [InlineData("12. Some. text", 12, "Some. text")]
        [InlineData("9999999999. A", 9999999999L, "A")]
        [InlineData("5. ", 5, "")] //empty string        
        public void When_Line_Is_Valid_Should_Parse(string input, long number, string text)
        {
            Entry entry = new Entry(input);
            Assert.Equal(input, entry.Line);
            Assert.Equal(number, entry.Number);
            Assert.Equal(text, entry.Text);
        }

        [Theory]
        [InlineData("a123. Some text")]
        [InlineData(". Some text")]
        [InlineData("123.Some text")]
        [InlineData("-123. Some text")] //signs not allowed - NumberStyles.None
        [InlineData("9223372036854775808. Some text")] //overflow long
        public void When_Line_Is_Invalid_Should_Throw(string line)
        {
            var exception = Assert.Throws<FormatException>(()=>
            {
                Entry entry = new Entry(line);
            });
            Assert.Contains(line, exception.Message); // short lines are shown in full
        }

        [Fact]
        public void When_Invalid_Line_Is_Long_Should_Show_Only_Its_Start()
        {
            string line = "1." + new string('x', 1000); // no ". " after the number

            var exception = Assert.Throws<FormatException>(() =>
            {
                Entry entry = new Entry(line);
            });

            Assert.Contains(line[..20], exception.Message);
            Assert.DoesNotContain(line, exception.Message);
            Assert.True(exception.Message.Length < 200, $"Message has {exception.Message.Length} chars");
        }

        [Theory]
        [InlineData("1. Apple", "2. Banana", -1)]  // text decides
        [InlineData("415. Apple", "1. Apple", 1)]  // same text → number decides
        [InlineData("2. Apple", "10. Apple", -1)]  // numeric, not text: 2 < 10
        [InlineData("1. Apple", "1. Apple", 0)]  // identical lines
        [InlineData("1. Apple", "1. apple", -1)]  // ordinal: 'A' 65 < 'a' 97
        [InlineData("1. Zebra", "1. apple", -1)]  // all uppercase before lowercase
        [InlineData("1. zebra", "1. Éclair", -1)]  // non-ASCII after ASCII
        [InlineData("1. Apple", "1. Apple pie", -1)]  // prefix sorts first
        [InlineData("5. Hello. World", "5. Hello", 1)]  // text may contain ". "
        [InlineData("9999999999. A", "1. A", 1)]  // number larger than int
        public void When_Comparing_Should_Compare_Text_Then_Number(string line1, string line2, int expected)
        {
            var left = new Entry(line1);
            var right = new Entry(line2);
            Assert.Equal(expected, Math.Sign(left.CompareTo(right)));
            Assert.Equal(-1 * expected, Math.Sign(right.CompareTo(left)));//symetry
        }


        [Fact]
        public void When_Sorting_List_Should_Order_By_Text_Then_Number()
        {
            string[] input =
            [
                "415. Apple",
            "30432. Something something something",
            "1. Apple",
            "32. Cherry is the best",
            "2. Banana is yellow",
        ];
            string[] expected =
            [
                "1. Apple",
            "415. Apple",
            "2. Banana is yellow",
            "32. Cherry is the best",
            "30432. Something something something",
        ];

            var entries = input.Select(line => new Entry(line)).ToList();
            entries.Sort();

            Assert.Equal(expected, entries.Select(e => e.Line));
        }
    }
}
