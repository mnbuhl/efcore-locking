```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.7.2 (25G332) [Darwin 25.6.0]
Unknown processor
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method           | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| ShortSql_NoTag   |  6.124 ns | 0.0809 ns | 0.0899 ns |  1.00 |    0.02 |         - |          NA |
| ShortSql_WithTag |  2.783 ns | 0.0415 ns | 0.0389 ns |  0.45 |    0.01 |         - |          NA |
| LongSql_NoTag    | 56.177 ns | 1.0916 ns | 1.0721 ns |  9.17 |    0.21 |         - |          NA |
| LongSql_WithTag  |  2.835 ns | 0.0798 ns | 0.0746 ns |  0.46 |    0.01 |         - |          NA |
