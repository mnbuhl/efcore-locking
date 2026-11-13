```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.7.2 (25G332) [Darwin 25.6.0]
Unknown processor
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method                           | Mean     | Error     | StdDev    | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |---------:|----------:|----------:|---------:|------:|--------:|-------:|----------:|------------:|
| Postgres_NoLock                  | 2.981 μs | 0.0588 μs | 0.1463 μs | 2.934 μs |  1.00 |    0.07 | 0.0343 |   4.16 KB |        1.00 |
| Postgres_ForUpdate               | 5.047 μs | 0.0813 μs | 0.0798 μs | 5.045 μs |  1.70 |    0.08 | 0.0305 |   6.02 KB |        1.45 |
| Postgres_ForUpdate_WithTimeout   | 5.023 μs | 0.0946 μs | 0.0839 μs | 5.024 μs |  1.69 |    0.08 | 0.0305 |   6.05 KB |        1.46 |
| Postgres_ForUpdate_MultipleTags  | 8.199 μs | 0.1175 μs | 0.1099 μs | 8.225 μs |  2.76 |    0.13 | 0.0610 |   9.16 KB |        2.20 |
| MySql_NoLock                     | 2.926 μs | 0.0522 μs | 0.0436 μs | 2.915 μs |  0.98 |    0.05 | 0.0305 |   4.46 KB |        1.07 |
| MySql_ForUpdate                  | 5.135 μs | 0.0946 μs | 0.1327 μs | 5.101 μs |  1.73 |    0.09 | 0.0305 |   6.33 KB |        1.52 |
| MySql_ForUpdate_MultipleTags     | 8.242 μs | 0.1130 μs | 0.1002 μs | 8.238 μs |  2.77 |    0.13 | 0.0610 |   9.47 KB |        2.28 |
| SqlServer_NoLock                 | 3.031 μs | 0.0589 μs | 0.0523 μs | 3.019 μs |  1.02 |    0.05 | 0.0305 |   4.48 KB |        1.08 |
| SqlServer_ForUpdate              | 5.089 μs | 0.0966 μs | 0.0807 μs | 5.076 μs |  1.71 |    0.08 | 0.0305 |   6.34 KB |        1.53 |
| SqlServer_ForUpdate_MultipleTags | 8.874 μs | 0.1576 μs | 0.2157 μs | 8.843 μs |  2.98 |    0.16 | 0.0610 |   9.48 KB |        2.28 |
