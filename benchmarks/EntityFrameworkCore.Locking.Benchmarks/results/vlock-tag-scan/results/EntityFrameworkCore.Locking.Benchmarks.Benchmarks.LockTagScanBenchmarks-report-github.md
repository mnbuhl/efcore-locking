```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.7.2 (25G332) [Darwin 25.6.0]
Unknown processor
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method                    | Scenario             | Mean         | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------- |--------------------- |-------------:|-----------:|----------:|------:|--------:|----------:|------------:|
| **Current_FullTextScan**      | **CustomTags_NoLock**    |    **73.495 ns** |  **0.2706 ns** | **0.2399 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Optimized_LeadingComments | CustomTags_NoLock    |    10.807 ns |  0.0747 ns | 0.0662 ns |  0.15 |    0.00 |         - |          NA |
|                           |                      |              |            |           |       |         |           |             |
| **Current_FullTextScan**      | **DeclarePrefix**        |     **4.817 ns** |  **0.0545 ns** | **0.0455 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Optimized_LeadingComments | DeclarePrefix        |     6.545 ns |  0.1161 ns | 0.1030 ns |  1.36 |    0.02 |         - |          NA |
|                           |                      |              |            |           |       |         |           |             |
| **Current_FullTextScan**      | **DeclarePrefix_NoLock** |    **73.789 ns** |  **1.0076 ns** | **0.9425 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Optimized_LeadingComments | DeclarePrefix_NoLock |    74.325 ns |  0.2058 ns | 0.1824 ns |  1.01 |    0.01 |         - |          NA |
|                           |                      |              |            |           |       |         |           |             |
| **Current_FullTextScan**      | **LeadingTag**           |     **2.711 ns** |  **0.0110 ns** | **0.0086 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Optimized_LeadingComments | LeadingTag           |     1.359 ns |  0.0175 ns | 0.0146 ns |  0.50 |    0.01 |         - |          NA |
|                           |                      |              |            |           |       |         |           |             |
| **Current_FullTextScan**      | **Long_NoTag**           |    **74.519 ns** |  **1.3896 ns** | **1.4869 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Optimized_LeadingComments | Long_NoTag           |     1.538 ns |  0.0601 ns | 0.0617 ns |  0.02 |    0.00 |         - |          NA |
|                           |                      |              |            |           |       |         |           |             |
| **Current_FullTextScan**      | **MultipleTags**         |     **6.027 ns** |  **0.1407 ns** | **0.1247 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Optimized_LeadingComments | MultipleTags         |    10.663 ns |  0.1577 ns | 0.1398 ns |  1.77 |    0.04 |         - |          NA |
|                           |                      |              |            |           |       |         |           |             |
| **Current_FullTextScan**      | **Short_NoTag**          |     **5.504 ns** |  **0.0774 ns** | **0.0686 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Optimized_LeadingComments | Short_NoTag          |     1.564 ns |  0.0384 ns | 0.0360 ns |  0.28 |    0.01 |         - |          NA |
|                           |                      |              |            |           |       |         |           |             |
| **Current_FullTextScan**      | **TimeoutPrefix**        |     **6.017 ns** |  **0.0737 ns** | **0.0689 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Optimized_LeadingComments | TimeoutPrefix        |     8.070 ns |  0.0593 ns | 0.0495 ns |  1.34 |    0.02 |         - |          NA |
|                           |                      |              |            |           |       |         |           |             |
| **Current_FullTextScan**      | **VeryLong_NoTag**       | **4,473.723 ns** | **10.5713 ns** | **8.8275 ns** | **1.000** |    **0.00** |         **-** |          **NA** |
| Optimized_LeadingComments | VeryLong_NoTag       |     9.658 ns |  0.1558 ns | 0.1381 ns | 0.002 |    0.00 |         - |          NA |
