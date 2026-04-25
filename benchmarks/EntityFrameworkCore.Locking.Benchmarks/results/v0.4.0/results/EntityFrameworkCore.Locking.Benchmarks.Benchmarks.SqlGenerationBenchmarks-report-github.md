```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.4.1 (25E253) [Darwin 25.4.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method                           | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Postgres_NoLock                  | 2.841 μs | 0.0266 μs | 0.0207 μs |  1.00 |    0.01 | 0.0343 |   4.16 KB |        1.00 |
| Postgres_ForUpdate               | 5.277 μs | 0.1024 μs | 0.1179 μs |  1.86 |    0.04 | 0.0305 |   6.02 KB |        1.45 |
| Postgres_ForUpdate_WithTimeout   | 5.291 μs | 0.0898 μs | 0.1229 μs |  1.86 |    0.04 | 0.0305 |   6.05 KB |        1.46 |
| Postgres_ForUpdate_MultipleTags  | 8.562 μs | 0.1566 μs | 0.1388 μs |  3.01 |    0.05 | 0.0610 |   9.16 KB |        2.20 |
| MySql_NoLock                     | 3.191 μs | 0.0638 μs | 0.0829 μs |  1.12 |    0.03 | 0.0381 |   4.76 KB |        1.14 |
| MySql_ForUpdate                  | 5.021 μs | 0.0459 μs | 0.0383 μs |  1.77 |    0.02 | 0.0305 |   6.33 KB |        1.52 |
| MySql_ForUpdate_MultipleTags     | 8.305 μs | 0.1003 μs | 0.0889 μs |  2.92 |    0.04 | 0.0610 |   9.47 KB |        2.28 |
| SqlServer_NoLock                 | 2.918 μs | 0.0281 μs | 0.0249 μs |  1.03 |    0.01 | 0.0343 |   4.48 KB |        1.08 |
| SqlServer_ForUpdate              | 5.039 μs | 0.0818 μs | 0.0725 μs |  1.77 |    0.03 | 0.0305 |   6.34 KB |        1.53 |
| SqlServer_ForUpdate_MultipleTags | 8.251 μs | 0.0702 μs | 0.0622 μs |  2.90 |    0.03 | 0.0610 |   9.48 KB |        2.28 |
