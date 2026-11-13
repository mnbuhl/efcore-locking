```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.7.2 (25G332) [Darwin 25.6.0]
Unknown processor
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method                | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Old_StartsWith_Empty  | 1.1035 ns | 0.0130 ns | 0.0109 ns |  1.00 |    0.01 |         - |          NA |
| New_Contains_Empty    | 0.3862 ns | 0.0107 ns | 0.0089 ns |  0.35 |    0.01 |         - |          NA |
| Old_StartsWith_Single | 1.9858 ns | 0.0590 ns | 0.0523 ns |  1.80 |    0.05 |         - |          NA |
| New_Contains_Single   | 6.0204 ns | 0.1452 ns | 0.1987 ns |  5.46 |    0.18 |         - |          NA |
| Old_StartsWith_Multi  | 2.8059 ns | 0.0665 ns | 0.0739 ns |  2.54 |    0.07 |         - |          NA |
| New_Contains_Multi    | 5.4496 ns | 0.0103 ns | 0.0086 ns |  4.94 |    0.05 |         - |          NA |
