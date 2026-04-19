```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.4.1 (25E253) [Darwin 25.4.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method                           | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Postgres_NoLock                  | 2.445 μs | 0.0212 μs | 0.0177 μs |  1.00 |    0.01 | 0.0305 |    3.8 KB |        1.00 |
| Postgres_ForUpdate               | 4.570 μs | 0.0593 μs | 0.0555 μs |  1.87 |    0.03 | 0.0305 |   5.13 KB |        1.35 |
| Postgres_ForUpdate_WithTimeout   | 4.851 μs | 0.0965 μs | 0.1836 μs |  1.98 |    0.08 | 0.0305 |   5.16 KB |        1.36 |
| Postgres_ForUpdate_MultipleTags  | 8.087 μs | 0.1475 μs | 0.2115 μs |  3.31 |    0.09 | 0.0610 |   7.67 KB |        2.02 |
| MySql_NoLock                     | 2.478 μs | 0.0293 μs | 0.0274 μs |  1.01 |    0.01 | 0.0305 |    4.1 KB |        1.08 |
| MySql_ForUpdate                  | 4.788 μs | 0.0850 μs | 0.1043 μs |  1.96 |    0.04 | 0.0305 |   5.43 KB |        1.43 |
| MySql_ForUpdate_MultipleTags     | 8.340 μs | 0.1636 μs | 0.1818 μs |  3.41 |    0.08 | 0.0610 |   7.98 KB |        2.10 |
| SqlServer_NoLock                 | 2.619 μs | 0.0521 μs | 0.0487 μs |  1.07 |    0.02 | 0.0305 |   4.12 KB |        1.08 |
| SqlServer_ForUpdate              | 4.851 μs | 0.0965 μs | 0.0903 μs |  1.98 |    0.04 | 0.0305 |   5.45 KB |        1.43 |
| SqlServer_ForUpdate_MultipleTags | 8.053 μs | 0.0659 μs | 0.0616 μs |  3.29 |    0.03 | 0.0610 |   7.99 KB |        2.10 |
