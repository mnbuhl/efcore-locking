```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.4.1 (25E253) [Darwin 25.4.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method                | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Old_StartsWith_Empty  | 1.0166 ns | 0.0163 ns | 0.0152 ns |  1.00 |    0.02 |         - |          NA |
| New_Contains_Empty    | 0.3772 ns | 0.0101 ns | 0.0095 ns |  0.37 |    0.01 |         - |          NA |
| Old_StartsWith_Single | 1.8758 ns | 0.0186 ns | 0.0165 ns |  1.85 |    0.03 |         - |          NA |
| New_Contains_Single   | 6.0173 ns | 0.0465 ns | 0.0435 ns |  5.92 |    0.10 |         - |          NA |
| Old_StartsWith_Multi  | 2.7227 ns | 0.0289 ns | 0.0270 ns |  2.68 |    0.05 |         - |          NA |
| New_Contains_Multi    | 5.9900 ns | 0.0491 ns | 0.0435 ns |  5.89 |    0.09 |         - |          NA |
