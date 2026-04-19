using BenchmarkDotNet.Attributes;
using EntityFrameworkCore.Locking.Internal;

namespace EntityFrameworkCore.Locking.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class InterceptorBenchmarks
{
    private const string ShortSqlNoTag = "SELECT \"Id\", \"Name\" FROM \"benchmark_entities\" WHERE \"Id\" > 0";
    private const string ShortSqlWithTag =
        "-- __efcore_locking:ForUpdate:Wait:\n"
        + "SELECT \"Id\", \"Name\" FROM \"benchmark_entities\" WHERE \"Id\" > 0 FOR UPDATE";
    private string _longSqlNoTag = null!;
    private string _longSqlWithTag = null!;

    [GlobalSetup]
    public void Setup()
    {
        var body = string.Join(
            "\nJOIN ",
            Enumerable.Repeat("\"benchmark_entities\" ON \"benchmark_entities\".\"Id\" = \"other\".\"Id\"", 10)
        );
        _longSqlNoTag = $"SELECT \"Id\", \"Name\" FROM \"benchmark_entities\" JOIN {body} WHERE \"Id\" > 0";
        _longSqlWithTag = $"-- __efcore_locking:ForUpdate:Wait:\n{_longSqlNoTag} FOR UPDATE";
    }

    [Benchmark(Baseline = true)]
    public bool ShortSql_NoTag() => ShortSqlNoTag.Contains(LockTagConstants.Prefix, StringComparison.Ordinal);

    [Benchmark]
    public bool ShortSql_WithTag() => ShortSqlWithTag.Contains(LockTagConstants.Prefix, StringComparison.Ordinal);

    [Benchmark]
    public bool LongSql_NoTag() => _longSqlNoTag.Contains(LockTagConstants.Prefix, StringComparison.Ordinal);

    [Benchmark]
    public bool LongSql_WithTag() => _longSqlWithTag.Contains(LockTagConstants.Prefix, StringComparison.Ordinal);
}
