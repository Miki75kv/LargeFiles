using System.Text;

namespace Sorter.Services
{
    public static class NewLineDetector
    {
        // Returns "\r\n" if the first line ends with CR LF, otherwise "\n"
        // (also for an empty file or a single line without a line ending).
        // Reads chars, not bytes, so a BOM and UTF-16 input are decoded the same way as by the main reader.
        public static string DetermineFirstLineEnding(string filePath)
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            int previous = -1;
            int current;
            while ((current = reader.Read()) != -1)
            {
                if (current == '\n')
                    return previous == '\r' ? "\r\n" : "\n";

                previous = current;
            }

            return "\n";
        }
    }
}
