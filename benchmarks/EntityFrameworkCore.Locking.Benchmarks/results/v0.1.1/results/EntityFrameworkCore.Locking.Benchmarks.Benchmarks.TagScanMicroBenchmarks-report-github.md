```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.4.1 (25E253) [Darwin 25.4.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method                | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Old_StartsWith_Empty  | 1.1472 ns | 0.0421 ns | 0.0394 ns |  1.00 |    0.05 |         - |          NA |
| New_Contains_Empty    | 0.4463 ns | 0.0343 ns | 0.0304 ns |  0.39 |    0.03 |         - |          NA |
| Old_StartsWith_Single | 1.6368 ns | 0.0427 ns | 0.0357 ns |  1.43 |    0.06 |         - |          NA |
| New_Contains_Single   | 6.0491 ns | 0.0893 ns | 0.0836 ns |  5.28 |    0.19 |         - |          NA |
| Old_StartsWith_Multi  | 2.5142 ns | 0.0514 ns | 0.0430 ns |  2.19 |    0.08 |         - |          NA |
| New_Contains_Multi    | 5.9007 ns | 0.1144 ns | 0.0955 ns |  5.15 |    0.19 |         - |          NA |
