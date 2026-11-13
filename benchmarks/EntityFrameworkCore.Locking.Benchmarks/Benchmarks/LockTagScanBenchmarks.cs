using BenchmarkDotNet.Attributes;
using EntityFrameworkCore.Locking.Internal;

namespace EntityFrameworkCore.Locking.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class LockTagScanBenchmarks
{
    private string _sql = null!;

    [Params(
        "Short_NoTag",
        "Long_NoTag",
        "VeryLong_NoTag",
        "CustomTags_NoLock",
        "LeadingTag",
        "MultipleTags",
        "TimeoutPrefix",
        "DeclarePrefix",
        "DeclarePrefix_NoLock"
    )]
    public string Scenario { get; set; } = "Short_NoTag";

    [GlobalSetup]
    public void Setup()
    {
        const string tag = "-- __efcore_locking:ForUpdate:Wait:500\n";
        var length = Scenario switch
        {
            "Short_NoTag" => 64,
            "VeryLong_NoTag" => 65536,
            _ => 1024,
        };
        var prefix = Scenario switch
        {
            "CustomTags_NoLock" => "-- custom-tag-a\n-- custom-tag-b\n",
            "LeadingTag" => tag,
            "MultipleTags" => "-- custom-tag-a\n-- custom-tag-b\n" + tag,
            "TimeoutPrefix" => "SET LOCAL lock_timeout = '500ms';\n" + tag,
            "DeclarePrefix" => "DECLARE @value int = 1;\n" + tag,
            "DeclarePrefix_NoLock" => "DECLARE @value int = 1;\n",
            _ => "",
        };
        const string select = "SELECT \"Id\", \"Name\" FROM \"benchmark_entities\" WHERE \"Name\" = '";
        _sql = prefix + select + new string('x', length - prefix.Length - select.Length - 1) + "'";
        if (Current_FullTextScan() != Optimized_LeadingComments())
            throw new InvalidOperationException("Scan results must match for the benchmark inputs.");
    }

    [Benchmark(Baseline = true)]
    public bool Current_FullTextScan() => _sql.Contains(LockTagConstants.Prefix, StringComparison.Ordinal);

    [Benchmark]
    public bool Optimized_LeadingComments() => LockTagConstants.FindTag(_sql) >= 0;
}
