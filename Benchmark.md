# Sorter benchmarks

## Hardware and software

| | |
|---|---|
| CPU | Intel Core i7-1165G7 @ 2.80 GHz (laptop), 4 cores / 8 threads |
| RAM | 32 GB, 3200 MT/s |
| Disk | Samsung MZALQ512HBLU, 512 GB NVMe SSD, split into C: (389 GB) and D: (85 GB) on the same physical drive |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 10.0.402, runtime 10.0.13 |

## How the numbers were measured

- Release build, Sorter.exe started directly from a terminal (not from Visual Studio, no debugger), on mains power
- Test files from `Generator.exe <file> <size-mb> --unique 2000 --seed 54`
- Input and output on D:, temp files in the system temp folder on C: (both on the same SSD, so they share its bandwidth)
- Default options unless the label says otherwise
- Each run's output is checked: single-threaded runs by size (same as the input), parallel runs by SHA-256 hash (identical to the single-threaded output)

## Final results

Final build (2026-10-27): parallel split (one reader, N sorting workers), single-threaded merge, workstation GC, default 100 MB chunks.
Note: the runs below used the earlier option `--max-chunk-size-mb`, where "100 MB" meant 104,857,600 characters. It is now `--max-chunk-mchars`, in plain millions of characters (default 100 = 100,000,000).
The sorting logic is unchanged since the parallel outputs were checked by hash (see the history below), so these runs weren't checked again.

| Configuration | Input | Split (s) | Merge (s) | Total (s) | Peak memory (MB) | Chunks | GC gen0 / gen1 / gen2 | GC pause (s) | Allocated (MB) |
|---|---|---|---|---|---|---|---|---|---|
| `--workers 1` | test-1GB.txt | 26.2 | 4.7 | 31.0 | 1,170 | 10 | 917 / 239 / 13 | 2.5 | 5,623 |
| default (8 workers) | test-1GB.txt | 13.3 | 4.9 | 18.3 | 3,542 | 10 | 912 / 234 / 8 | 2.5 | 6,183 |
| default (8 workers) | test-10GB.txt | 125.3 | 74.1 | 199.6 | 5,234 | 98 | 9,069 / 2,294 / 25 | 30.2 | 55,317 |
| default (8 workers) | test-50GB.txt (on C:) | 795.9 | 546.3 | 1,342.4 | 5,246 | 486 | 45,245 / 13,963 / 105 | 240.4 | 273,196 |

- **Memory doesn't grow with the input:** the peak is the same for 10 GB and 50 GB (about 5.2 GB). It's bounded by the 9 lists in circulation
  (8 workers + 1, about 3.3 GB of live data) plus the strings the GC hasn't collected yet. On 1 GB the run ends before that backlog builds up.
- **Time grows a little faster than the input:** 50 GB took 6.7× as long as 10 GB, for 5× the data.
  - The merge has to choose among 486 files per line instead of 98 (about 9 comparisons instead of 6.6).
  - In the split, both reading and sorting were slower per chunk than on 10 GB. Likely causes are the laptop throttling under 13 minutes
    of full load, and the input, temp files and output sharing one SSD. These runs can't separate the two.
- **The 50 GB run is on C:**, because D: didn't have room for 150 GB (input, temp files and output). It's the same physical SSD, so the
  conditions are otherwise the same.

## Development history

Runs made while the program was being built, in order. They explain the design decisions but don't all use the final build.

| Date | Label | Input | Run | Split (s) | Merge (s) | Total (s) | Peak memory (MB) | Chunks | GC gen0 / gen1 / gen2 | GC pause (s) | Allocated (MB) | Output check |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-10-26 | single-threaded | test-1GB.txt | 1 | 28.5 | 4.5 | 33.1 | 550 | 10 | 926 / 250 / 22 | 2.3 | 5,543 | ok |
| 2026-10-26 | single-threaded | test-1GB.txt | 2 | 28.7 | 4.5 | 33.4 | 558 | 10 | 926 / 250 / 22 | 2.4 | 5,543 | ok |
| 2026-10-26 | single-threaded | test-1GB.txt | 3 | 32.6 | 5.4 | 38.2 | 573 | 10 | 926 / 250 / 22 | 2.5 | 5,543 | ok |
| 2026-10-26 | single-threaded | test-10GB.txt | 1 | 370.6 | 75.7 | 446.4 | 780 | 98 | 9,260 / 2,513 / 218 | 32.3 | 54,677 | ok |
| 2026-10-27 | single-threaded, `--max-chunk-size-mb 50`, on battery (balanced power mode): times not comparable | test-1GB.txt | 1 | 71.7 | 12.5 | 84.2 | 351 | 20 | 945 / 277 / 45 | 7.2 | 5,553 | ok |
| 2026-10-27 | parallel split, 8 workers | test-1GB.txt | 1 | 13.7 | 4.8 | 18.6 | 3,542 | 10 | 912 / 234 / 8 | 2.7 | 6,183 | ok (hash) |
| 2026-10-27 | parallel split, `--workers 4` | test-1GB.txt | 1 | 16.0 | 4.8 | 20.9 | 2,817 | 10 | 913 / 235 / 9 | 2.7 | 5,863 | ok (hash) |
| 2026-10-27 | parallel split, 8 workers | test-10GB.txt | 1 | 124.4 | 83.2 | 207.7 | 5,232 | 98 | 9,069 / 2,295 / 25 | 29.7 | 55,317 | ok (hash) |
| 2026-10-27 | parallel split, 8 workers, Server GC | test-1GB.txt | 1 | 12.4 | 4.8 | 17.2 | 3,832 | 10 | 68 / 18 / 6 | 1.5 | 6,183 | ok (hash) |
| 2026-10-27 | parallel split, 8 workers, Server GC (allocated figure not reliable, see note) | test-10GB.txt | 1 | 113.2 | 96.5 | 210.0 | 7,425 | 98 | 4,584 / 58 / 26 | 16.3 | 30,455 | ok (hash) |
| 2026-10-27 | parallel split, `--workers 1`, Server GC (by mistake: stale build) | test-1GB.txt | 1 | 24.3 | 4.8 | 29.2 | 2,136 | 10 | 67 / 17 / 8 | 1.2 | 4,391 | not checked |

Note on Server GC: the program allocates the same objects whatever GC it runs under, so the drop from about 55 GB to 30 GB allocated
for 10 GB can't be real. Under Server GC `GC.GetTotalAllocatedBytes()` (not precise) apparently undercounts.
