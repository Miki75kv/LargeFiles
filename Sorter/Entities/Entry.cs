using System.Globalization;

namespace Sorter.Entities
{
    public readonly struct Entry : IComparable<Entry>
    {
        public readonly string Line;
        public readonly long Number;
        public readonly int TextStart;
        public ReadOnlySpan<char> Text => Line.AsSpan(TextStart);

        public Entry(string line)
        {
            int dotIndex = line.IndexOf(". ", StringComparison.Ordinal);
            //do not allow dot to be in zero index or -1
            if ((dotIndex <= 0) ||
            !long.TryParse(line.AsSpan().Slice(0,dotIndex), NumberStyles.None, CultureInfo.InvariantCulture, out Number))
                throw new FormatException($"Line must be in format \"<number>. <text>\". Line: {ShortenForMessage(line)}");

            Line = line;
            TextStart = dotIndex + 2;
        }

        private static string ShortenForMessage(string line)
        {
            // A file without newlines reads as one huge "line"; only its start goes into the error message
            const int maxCharsInMessage = 100;
            return line.Length <= maxCharsInMessage ? line : line[..maxCharsInMessage] + "...";
        }

        public int CompareTo(Entry other)
        {
            int stringComparison = Text.CompareTo(other.Text, StringComparison.Ordinal);
            if (stringComparison == 0) return Number.CompareTo(other.Number);
            else return stringComparison;
        }
    }
}
