# Leading lock-tag scan comparison

Rerun on 13 November 2026 after hardening the fallback for unfamiliar SQL prefixes. This compares the original ordinal `string.Contains` scan with the actual `LockTagConstants.FindTag` helper introduced by this change, using identical SQL strings in the same BenchmarkDotNet run. Base revision: `51ef9ff558aa4dd5aa3de7830b1b3fa1ac88a206` (v0.6.1 source plus published release benchmark results).

## Reproduce

On this PR branch:

```bash
dotnet run -c Release --project benchmarks/EntityFrameworkCore.Locking.Benchmarks -- --version=lock-tag-scan --filter '*LockTagScanBenchmarks*'
```

The label selects `results/vlock-tag-scan/`; it does not select a package version. Both implementations are measured side by side with the default job and memory diagnoser. Setup verifies equal detection results for every benchmark input.

## Environment

BenchmarkDotNet 0.15.8, .NET SDK 10.0.201, .NET runtime 10.0.5 Arm64 RyuJIT, macOS Tahoe 26.7.2 (25G332), Darwin 25.6.0. BenchmarkDotNet reported an unknown processor. The benchmark project retains its existing EF Core 9 providers.

## Results

| Scenario | Total SQL length | Current full-text scan | Leading-comment scan |
| --- | ---: | ---: | ---: |
| No tag, short SELECT | 64 chars | 5.504 ns | 1.564 ns |
| No tag, long SELECT | 1,024 chars | 74.519 ns | 1.538 ns |
| No tag, very long SELECT | 65,536 chars | 4,473.723 ns | 9.658 ns |
| Two custom tags, no lock | 1,024 chars | 73.495 ns | 10.807 ns |
| Leading lock tag | 1,024 chars | 2.711 ns | 1.359 ns |
| Lock tag after two custom tags | 1,024 chars | 6.027 ns | 10.663 ns |
| PostgreSQL timeout prefix and lock tag | 1,024 chars | 6.017 ns | 8.070 ns |
| DECLARE prefix and lock tag | 1,024 chars | 4.817 ns | 6.545 ns |
| DECLARE prefix, no lock | 1,024 chars | 73.789 ns | 74.325 ns |

The untagged SELECT cases at 1 KB and 64 KB improved by approximately 48× and 463× respectively in this rerun. Neither method reported managed allocations. All 18 benchmark cases completed successfully.

There are measured tradeoffs: a lock tag after two custom tags costs about 4.6 ns more per scan, and the timeout-prefixed case costs about 2.1 ns more. The conservative `DECLARE` fallback costs about 1.7 ns extra with a lock tag; without a tag it retains approximately the same full-text scan cost (73.789 vs 74.325 ns). This rerun emitted no BenchmarkDotNet warnings. The gains for long untagged SELECT queries are much larger than the observed measurement variability.

These are scan microbenchmarks, not end-to-end interceptor or database timings. Execution still includes query generation, database work, and other interception logic. Results depend on runtime, hardware, and input layout; the ratio does not predict application throughput.

## Behavior and compatibility

The helper scans whitespace and leading `--` query comments. It skips the remaining body only when it encounters a complete `SELECT` keyword followed by whitespace or end-of-input. Both transaction validation and reader wrapping use it. Marker text inside an ordinary `SELECT` body or string literal no longer acts as a locking query tag.

All other prefixes retain the original full-text search. This includes `DECLARE`, `EXEC`, `WITH`, DML, timeout `SET` commands (including tab-separated forms), block comments, parentheses, byte-order marks, and unfamiliar tokens, even after custom header comments. Token-boundary checks prevent `SELECTED`, `SELECT_value`, or `SELECT1` from being treated as `SELECT`. These fallback layouts keep their original detection semantics and full-text scan costs.

EF-generated headers, including multiple/multiline tags and tags on composed raw SQL, are covered by tests across EF 8, 9, and 10. Regression tests also verify transaction validation and sync/async reader wrapping after unfamiliar preambles. The new `DECLARE` benchmarks check tagged and untagged fallback commands.

This is not a general SQL parser. A custom batch that itself starts with an ordinary `SELECT` still needs its lock tag in the leading comments: a tag after that first statement or inside the `SELECT` body is not detected. This remaining header contract is deliberate; the fallback does not claim compatibility with arbitrary `SELECT`-first batches.

## Exports

- [Full comparison with errors, standard deviations, and ratios](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.LockTagScanBenchmarks-report-github.md)
- [CSV](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.LockTagScanBenchmarks-report.csv)
- [HTML](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.LockTagScanBenchmarks-report.html)
