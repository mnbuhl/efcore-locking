# Leading lock-tag scan comparison

Measured on 13 November 2026. This compares the original ordinal `string.Contains` scan with the actual `LockTagConstants.FindTag` helper introduced by this change, using identical SQL strings in the same BenchmarkDotNet run. Base revision: `51ef9ff558aa4dd5aa3de7830b1b3fa1ac88a206` (v0.6.1 source plus published release benchmark results).

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
| No tag, short SQL | 64 chars | 5.659 ns | 1.353 ns |
| No tag, long SQL | 1,024 chars | 74.719 ns | 1.261 ns |
| No tag, very long SQL | 65,536 chars | 4,540.597 ns | 9.730 ns |
| Two custom tags, no lock | 1,024 chars | 75.805 ns | 10.865 ns |
| Leading lock tag | 1,024 chars | 2.781 ns | 1.401 ns |
| Lock tag after two custom tags | 1,024 chars | 5.984 ns | 11.087 ns |
| PostgreSQL timeout prefix and lock tag | 1,024 chars | 6.425 ns | 7.672 ns |

The untagged 1 KB and 64 KB cases improved by approximately 59× and 467× respectively in this run. Neither method reported managed allocations. All 14 benchmark cases completed successfully.

There are measured tradeoffs: a lock tag after two custom tags costs about 5.1 ns more per scan, and the timeout-prefixed case costs about 1.2 ns more. BenchmarkDotNet flagged the optimized multiple-tag case as bimodal (mValue 3.33); its report includes the error and standard deviation. The gain for long untagged SQL is much larger than the observed measurement variability.

These are scan microbenchmarks, not end-to-end interceptor or database timings. Execution still includes query generation, database work, and other interception logic. Results depend on runtime, hardware, and input layout; the ratio does not predict application throughput.

## Behavior and compatibility

The helper scans whitespace and leading `--` query comments, stopping when the SQL body begins. Both transaction validation and reader wrapping use it. Marker text inside an ordinary SQL body or string literal no longer acts as a locking query tag.

Commands beginning with `SET ` (case-insensitive) or `/*` after leading comments/whitespace retain the original full-text search. This preserves all three providers' timeout preambles without introducing a SQL parser. Such fallback layouts still have full-text scan costs, and retain the original marker detection semantics.

EF-generated headers, including multiple/multiline tags and tags on composed raw SQL, are covered by tests across EF 8, 9, and 10. A custom interceptor that prepends another kind of SQL statement before a query's tag must preserve a leading tag header for this optimized path to recognize it. Searching arbitrary SQL bodies is outside the optimized header contract.

## Exports

- [Full comparison with errors, standard deviations, and ratios](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.LockTagScanBenchmarks-report-github.md)
- [CSV](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.LockTagScanBenchmarks-report.csv)
- [HTML](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.LockTagScanBenchmarks-report.html)
