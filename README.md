# Large file sorter

Test task: sort a large text file with lines in the form `Number. String`, first by the string and then by the number. The solution has two .NET 10 console apps:

- **Generator** creates a test file of a given size.
- **Sorter** sorts the file with an external merge sort, so its memory usage doesn't depend on the file size.

On my laptop (i7-1165G7, 4 cores / 8 threads, 32 GB RAM, NVMe SSD) the Sorter sorts 1 GB in 20 s, 10 GB in 3 min 21 s and 50 GB in 19 to 20 minutes, using up to about 6 GB of RAM.

## Build

Requires the .NET 10 SDK.

```
dotnet build -c Release
dotnet test
```

The executables are in `Generator/bin/Release/net10.0/` and `Sorter/bin/Release/net10.0/`.

## Usage

```
Generator.exe test-10GB.txt 10240
Sorter.exe test-10GB.txt
```

The sorted file is written next to the input as `test-10GB-sorted.txt`.

### Generator

```
Usage: Generator <output-file> <size-mb> [--unique <int>] [--seed <int>]
  <output-file>  path of the file to create (overwritten if it exists); its folder must exist
  <size-mb>      target file size in MB; the file ends at most one line short of it
Options:
  --unique       number of distinct text parts to repeat across lines (default 1000, max 50000)
  --seed         seed for random generation; the same seed gives the same file
                 (default: random, printed at the end)
```

Numbers are random, from 1 to 2,147,483,646. A fixed set of text parts (1 to 5 words each) is created first and then reused at random, so many lines share the same text. The word list includes words that test the sort order: the same word in different case (`Apple`, `apple`, `APPLE`), prefixes (`App`, `Apples`), non-ASCII words (`Éclair`, `Ölmühle`), and dots and digits inside the text (`Mr.`, `R2-D2`). The file is UTF-8 without BOM, with `\n` line endings.

### Sorter

```
Usage: Sorter <input-file> [options]
  <input-file>                file to sort, lines in the form "<number>. <text>"
                              (UTF-8, or UTF-16/UTF-32 with a byte order mark)
Options:
  --output-file <path>        result file, overwritten if it exists; its folder must exist
                              (default: <input-name>-sorted<ext> next to the input file);
                              written as UTF-8 with the line ending of the input's first line
  --temp-dir <folder>         existing folder for temporary chunk files (default: system temp folder)
  --workers <int>             number of sorting workers (default: CPU count, at most 8; max 64)
  --max-chunk-mchars <int>    max text per chunk in millions of characters, newlines not counted
                              (default 100, max 1024); each worker holds one chunk in memory
  --merge-memory-mb <int>     total memory for the merge's read buffers, in MB (default 3072)
  --verbose                   also print timings for every chunk and a summary per worker
```

At the start the Sorter prints the settings it uses and the temp folder; at the end it prints the timings, peak memory and GC statistics. Exit codes: 0 success, 1 error during sorting, 2 invalid arguments.

## Input format and sort order

Each line must be `<number>. <text>`: a non-negative integer that fits in a `long`, a dot and a space, and the text. The text can be empty (`1. ` is valid). An empty or malformed line stops the sort with an error; no lines are skipped.

The input is read as UTF-8. A file that starts with a byte order mark is read in that encoding, so UTF-16 and UTF-32 files with a BOM work too. The output is always UTF-8 without a BOM. Lines can end with `\n` or `\r\n`; the output uses the same line ending as the first line of the input.

Lines are sorted by the text and then by the number. The text is compared ordinally (by UTF-16 code units), which is case-sensitive: `APPLE` < `Apple` < `Banana` < `apple` < `Éclair`. I chose ordinal comparison because it's fast and gives the same result regardless of culture settings. Duplicate lines are kept.

## How it works

The Sorter works in two phases.

**Split.** One reader thread reads the input and fills a list with lines until it reaches the chunk size. The full list goes to a queue, where a worker thread takes it, parses the lines, sorts them and writes them to a temporary chunk file. Then the worker gives the empty list back to the reader. There are always workers + 1 lists (one being filled by the reader and one per worker). They are allocated once and reused, so memory depends on the number of workers and the chunk size, not on the file size.

The chunk size is the file size divided by the number of workers, but at least 1 million and at most `--max-chunk-mchars` million characters (100 million by default). A small file is split evenly so each worker gets one chunk; a large file is split into chunks of the maximum size.

**Merge.** The chunk files are merged into the output with a k-way merge: a priority queue holds the current line of every chunk, and the smallest one is written and replaced by the next line from the same chunk.

The temp files go to a `sorter-<guid>` folder that is deleted at the end, also when the sort fails. If the reader or a worker fails, the other threads are cancelled and the original error is reported.

## Performance

### Test setup

| | |
|---|---|
| CPU | Intel Core i7-1165G7 @ 2.80 GHz (laptop), 4 cores / 8 threads |
| RAM | 32 GB, 3200 MT/s |
| Disk | Samsung MZALQ512HBLU, 512 GB NVMe SSD |
| OS / .NET | Windows 11 Pro 10.0.26300, .NET 10.0.13 |

- Release build, run from the terminal, laptop on AC power with the Windows power mode set to Best performance.
- Test files generated with `Generator.exe <file> <size-mb> --unique 2000 --seed 54`.

### Results with default settings (8 workers, 100 million characters per chunk)

| Input | Chunks | Split | Merge | Total | Peak memory | GC pause |
|---|---|---|---|---|---|---|
| 1 GB | 11 | 14.6 s | 5.5 s | 20.2 s | 3,671 MB | 2.7 s (13%) |
| 10 GB | 103 | 128.5 s | 72.0 s | 200.7 s | 5,506 MB | 29.9 s (15%) |
| 50 GB, run 1 | 510 | 705.1 s | 484.2 s | 1,189.5 s | 6,045 MB | 163.0 s (14%) |
| 50 GB, run 2 | 510 | 655.3 s | 491.8 s | 1,150.4 s | 5,397 MB | 159.3 s (14%) |

The 10 GB outputs with 4, 6 and 8 workers have the same SHA-256 hash.

From 10 GB to 50 GB the time grew about 5.8× for 5× the data. The split grows almost linearly; the merge grows a bit faster, because every line is compared among more chunks (log₂ 510 ≈ 9 instead of log₂ 103 ≈ 6.7). Memory stays about the same.

I couldn't test 100 GB on this laptop, because it needs about 300 GB of free disk space (input, temp files and output). Extrapolating the same way, I'd expect about 40–45 minutes and about 6 GB of RAM.

### Number of workers (10 GB)

| Workers | Split | Merge | Total | Peak memory |
|---|---|---|---|---|
| 4 | 159.8 s | 72.8 s | 232.7 s | 2,987 MB |
| 6 | 132.9 s | 73.2 s | 206.3 s | 4,306 MB |
| 8 (default) | 128.5 s | 72.0 s | 200.7 s | 5,506 MB |

The reader is the slowest part of the split: on a single thread it reads and decodes the whole file and creates a string for every line. From about 6 workers on, the workers are waiting for the reader, so adding more mostly adds memory. With 4 workers they can't keep up and the split is 24% slower. The merge takes the same time in all cases, because it's single-threaded and the number of chunks doesn't depend on the number of workers.

The default is capped at 8 workers. I preferred speed, since running time is part of the evaluation; with `--workers 6` the sort is 3% slower but uses 1.2 GB less memory.

### Memory

There are workers + 1 chunks in memory at the same time. One character takes about 3.5 bytes: 2 bytes because .NET strings are UTF-16, plus about 48 bytes per line for the string header and the entry with the parsed number, which adds 1.5 bytes per character for lines of 32 characters on average. The garbage collector frees the strings of already written chunks with a delay, so the peak is about 1.8× that. In practice:

peak memory ≈ (workers + 1) × `--max-chunk-mchars` × 6 MB

For the defaults that's 9 × 100 × 6 MB = 5,400 MB (measured 5,506 MB on 10 GB, and 5,397 and 6,045 MB in two runs on 50 GB; the peak varies a little with the timing of garbage collections). To use less memory, lower `--workers` or `--max-chunk-mchars`. Smaller chunks mean more chunk files, which makes the merge a bit slower.

### Disk space

The temp files take about as much space as the input, and so does the output, so the sort needs about 2× the input size of free space. The temp files can be put on another drive with `--temp-dir`.

## Known limitations

- If the Sorter is stopped with Ctrl+C, the temp folder isn't deleted. Its path is printed at the start.
- Free disk space isn't checked before the sort starts.
- Invalid UTF-8 bytes are replaced with U+FFFD instead of causing an error, and a single `\r` is treated as a line break (that's how .NET's `StreamReader` works).
- The output is written directly to the target file, so a failed merge leaves a partial file.
- The merge opens all chunk files at once. With default settings that's about 1,000 files for 100 GB, which is fine, but very small chunks on a huge file would need a merge in several passes.

## Possible improvements

- Reduce the GC work. Now every line is a separate string that lives until its chunk is written, and the program is paused for garbage collection about 15% of the time, mostly because these strings are moved between GC generations. Reading each chunk's text into one large char array and keeping only the offsets in the entries would avoid almost all of it.
- Read the input with several threads, each from a different part of the file. In UTF-8 the `\n` byte never appears inside a character, so the file can be split at any position and each reader starts at the next line break. This makes sense only after the previous change; with a string per line the GC would become the bottleneck.
- Write the output under a temporary name and rename it when the merge finishes.
- Merge in several passes when there are very many chunks.
