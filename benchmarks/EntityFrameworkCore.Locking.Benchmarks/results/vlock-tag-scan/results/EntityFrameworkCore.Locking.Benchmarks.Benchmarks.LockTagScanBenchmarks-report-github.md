```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.7.2 (25G332) [Darwin 25.6.0]
Unknown processor
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a


```
| Method                    | Scenario          | Mean         | Error      | StdDev     | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------- |------------------ |-------------:|-----------:|-----------:|------:|--------:|----------:|------------:|
| **Current_FullTextScan**      | **CustomTags_NoLock** |    **75.805 ns** |  **1.5167 ns** |  **2.1753 ns** |  **1.00** |    **0.04** |         **-** |          **NA** |
| Optimized_LeadingComments | CustomTags_NoLock |    10.865 ns |  0.1414 ns |  0.1323 ns |  0.14 |    0.00 |         - |          NA |
|                           |                   |              |            |            |       |         |           |             |
| **Current_FullTextScan**      | **LeadingTag**        |     **2.781 ns** |  **0.0321 ns** |  **0.0251 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Optimized_LeadingComments | LeadingTag        |     1.401 ns |  0.0431 ns |  0.0382 ns |  0.50 |    0.01 |         - |          NA |
|                           |                   |              |            |            |       |         |           |             |
| **Current_FullTextScan**      | **Long_NoTag**        |    **74.719 ns** |  **1.4900 ns** |  **1.3938 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Optimized_LeadingComments | Long_NoTag        |     1.261 ns |  0.0507 ns |  0.0774 ns |  0.02 |    0.00 |         - |          NA |
|                           |                   |              |            |            |       |         |           |             |
| **Current_FullTextScan**      | **MultipleTags**      |     **5.984 ns** |  **0.0697 ns** |  **0.0617 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Optimized_LeadingComments | MultipleTags      |    11.087 ns |  0.2462 ns |  0.3370 ns |  1.85 |    0.06 |         - |          NA |
|                           |                   |              |            |            |       |         |           |             |
| **Current_FullTextScan**      | **Short_NoTag**       |     **5.659 ns** |  **0.1030 ns** |  **0.0913 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Optimized_LeadingComments | Short_NoTag       |     1.353 ns |  0.0414 ns |  0.0323 ns |  0.24 |    0.01 |         - |          NA |
|                           |                   |              |            |            |       |         |           |             |
| **Current_FullTextScan**      | **TimeoutPrefix**     |     **6.425 ns** |  **0.1150 ns** |  **0.0961 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Optimized_LeadingComments | TimeoutPrefix     |     7.672 ns |  0.0917 ns |  0.0813 ns |  1.19 |    0.02 |         - |          NA |
|                           |                   |              |            |            |       |         |           |             |
| **Current_FullTextScan**      | **VeryLong_NoTag**    | **4,540.597 ns** | **79.4486 ns** | **70.4291 ns** | **1.000** |    **0.02** |         **-** |          **NA** |
| Optimized_LeadingComments | VeryLong_NoTag    |     9.730 ns |  0.2137 ns |  0.2461 ns | 0.002 |    0.00 |         - |          NA |
