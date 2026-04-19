```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.4.1 (25E253) [Darwin 25.4.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method           | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| ShortSql_NoTag   |  6.135 ns | 0.0493 ns | 0.0385 ns |  1.00 |    0.01 |         - |          NA |
| ShortSql_WithTag |  2.788 ns | 0.0406 ns | 0.0360 ns |  0.45 |    0.01 |         - |          NA |
| LongSql_NoTag    | 55.066 ns | 0.6146 ns | 0.5449 ns |  8.98 |    0.10 |         - |          NA |
| LongSql_WithTag  |  3.047 ns | 0.0224 ns | 0.0199 ns |  0.50 |    0.00 |         - |          NA |
