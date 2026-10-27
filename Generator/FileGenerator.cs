using System.Text;

namespace Generator
{
    public static class FileGenerator
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private  const int MB = 1024 * 1024;
        public const int DefaultUniquePhrasesCount = 1000;
        public const int MaxUniquePhrasesCount = 50_000;
        private static readonly string[] Words =
        [
            // Same word in different case. Ordinal compare puts "Apple" before "apple" ('A' = 65 < 'a' = 97)
            "Apple", "apple", "APPLE", "App", "Apples", "Applesauce",
            "Banana", "banana",
            "Cherry", "cherry",

            // Non-ASCII: sort after all ASCII letters in ordinal order, and take 2+ bytes in UTF-8
            "Šljiva", "Éclair", "éclair", "Ölmühle", "Smørrebrød", "Café",

            //Dots
            "Mr.", "Dr.", "Step 2.",

            // Digits and punctuation inside the text
            "1st", "42", "R2-D2", "e-mail",

            // Ordinary words
            "is", "the", "best", "yellow", "red", "green", "blue", "big", "small",
            "something", "nothing", "everything", "today", "tomorrow", "quick", "lazy", "dog", "fox"
        ];
        public static (long Bytes, long Lines) Generate(string outputFilePath, long targetBytes, int uniquePhrasesCount, int randomSeed)
        {
            Random random = new Random(randomSeed);
            var seen = new HashSet<string>();
            var phrases = new List<(string Phrase, int PhraseBytes)>(uniquePhrasesCount);
            while (phrases.Count < uniquePhrasesCount)
            {
                string phrase = BuildRandomPhrase(random);
                if (seen.Add(phrase))
                    phrases.Add((phrase, Encoding.UTF8.GetByteCount(phrase)));
            }

            long bytesWritten = 0;
            long linesWritten = 0;
            Span<char> numberBuffer = stackalloc char[20];
            using (var writer = new StreamWriter(
                outputFilePath,
                Utf8NoBom,
                new FileStreamOptions
                {
                    Mode = FileMode.Create, // overwrite existing file
                    Access = FileAccess.Write,
                    BufferSize = 1024 * 1024        // FileStream buffer: 1 MB writes to disk
                }))
            {
                while(true)
                {
                    var number = random.Next(1, int.MaxValue);
                    (string phrase, int phraseBytes) = phrases[random.Next(phrases.Count)];

                    number.TryFormat(numberBuffer, out int digits);
                    int lineBytes  = digits + 2 + phraseBytes +1;//number + 2 (". ") + phrase + 1(newline)
                    if (bytesWritten + lineBytes > targetBytes)
                        break;
                    writer.Write(numberBuffer[..digits]);
                    writer.Write(". ");
                    writer.Write(phrase);
                    writer.Write('\n');
                    bytesWritten += lineBytes;
                    linesWritten++;
                }
            }

            return (bytesWritten, linesWritten);
        }

        private static string BuildRandomPhrase(Random random)
        {
            return string.Join(" ",
                    Enumerable.Range(0, random.Next(1, 6)) //phrase contains between 1 to 5 words
                    .Select(_ => Words[random.Next(Words.Length)]));
        }
    }
}
