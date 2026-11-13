# v0.6.1 benchmark results

Measured on 13 November 2026 from release tag `v0.6.1`, commit `03c00fc0ad177babbba9e6cb1776eb174445d3d9`, using the unchanged benchmark suite and project references to the release source.

## Reproduce

```bash
git checkout v0.6.1
dotnet run -c Release --project benchmarks/EntityFrameworkCore.Locking.Benchmarks -- --version=0.6.1 --filter '*'
```

`--version` labels the output directory; checking out the release tag selects the code being measured. The run uses BenchmarkDotNet's default job and memory diagnoser.

## Environment

- BenchmarkDotNet 0.15.8; .NET SDK 10.0.201; .NET runtime 10.0.5, Arm64 RyuJIT.
- macOS Tahoe 26.7.2 (25G332), Darwin 25.6.0. BenchmarkDotNet reported the processor as unknown.
- EF Core / SQL Server provider 9.0.6; Npgsql provider 9.0.4; Pomelo provider 9.0.0, as resolved by the release benchmark project.

## Results

| Provider | No lock | ForUpdate | Added time | Allocated: no lock → ForUpdate |
| --- | ---: | ---: | ---: | ---: |
| PostgreSQL | 2.981 μs | 5.047 μs | 2.066 μs | 4.16 KB → 6.02 KB |
| MySQL | 2.926 μs | 5.135 μs | 2.209 μs | 4.46 KB → 6.33 KB |
| SQL Server | 3.031 μs | 5.089 μs | 2.058 μs | 4.48 KB → 6.34 KB |

Added time is the difference between each provider's reported means. The generated SQL generation report uses `Postgres_NoLock` as the baseline for every row, so its ratio column is not a per-provider locking overhead ratio.

SQL string tag scans measured 2.783–56.177 ns with no managed allocations reported. These are `string.Contains` microbenchmarks, not measurements of the complete interceptor.

The SQL generation cases build queries and call `ToQueryString()` after warming the compiled-query cache. They include query construction and SQL generation; they do not connect to a database or measure network latency, transaction validation, lock contention, or advisory locks. Tag scan comparisons exercise standalone lookup strategies, not two library versions.

Historical results were collected in separate runs and on a different macOS version. This run alone does not establish a performance improvement or regression against those versions.

BenchmarkDotNet flagged a multimodal distribution for `New_Contains_Single`; interpret that microbenchmark with its reported variability. The reported multimodality value was 3.17.

## Reports

- [SQL generation](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.SqlGenerationBenchmarks-report-github.md)
- [SQL string tag scans](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.InterceptorBenchmarks-report-github.md)
- [Tag lookup comparisons](results/EntityFrameworkCore.Locking.Benchmarks.Benchmarks.TagScanMicroBenchmarks-report-github.md)

CSV and HTML exports are included beside the Markdown reports.
