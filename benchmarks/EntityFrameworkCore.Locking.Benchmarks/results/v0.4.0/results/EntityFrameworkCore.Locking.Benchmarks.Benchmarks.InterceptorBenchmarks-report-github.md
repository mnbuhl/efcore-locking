```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.4.1 (25E253) [Darwin 25.4.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method           | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| ShortSql_NoTag   |  6.272 ns | 0.0845 ns | 0.0749 ns |  1.00 |    0.02 |         - |          NA |
| ShortSql_WithTag |  2.807 ns | 0.0269 ns | 0.0239 ns |  0.45 |    0.01 |         - |          NA |
| LongSql_NoTag    | 55.605 ns | 0.6941 ns | 0.6153 ns |  8.87 |    0.14 |         - |          NA |
| LongSql_WithTag  |  2.764 ns | 0.0318 ns | 0.0297 ns |  0.44 |    0.01 |         - |          NA |
