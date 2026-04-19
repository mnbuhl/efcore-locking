using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using EntityFrameworkCore.Locking.Benchmarks.Contexts;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class SqlGenerationBenchmarks
{
    private PostgresBenchmarkDbContext _pg = null!;
    private MySqlBenchmarkDbContext _my = null!;
    private SqlServerBenchmarkDbContext _ms = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pg = new PostgresBenchmarkDbContext();
        _my = new MySqlBenchmarkDbContext();
        _ms = new SqlServerBenchmarkDbContext();

        // Warm the EF Core compiled-query cache so we measure VisitSelect overhead,
        // not the one-time query compilation cost.
        _ = _pg.Items.Where(x => x.Id > 0).ToQueryString();
        _ = _pg.Items.Where(x => x.Id > 0).ForUpdate().ToQueryString();
        _ = _my.Items.Where(x => x.Id > 0).ToQueryString();
        _ = _my.Items.Where(x => x.Id > 0).ForUpdate().ToQueryString();
        _ = _ms.Items.Where(x => x.Id > 0).ToQueryString();
        _ = _ms.Items.Where(x => x.Id > 0).ForUpdate().ToQueryString();
    }

    [Benchmark(Baseline = true)]
    public string Postgres_NoLock() => _pg.Items.Where(x => x.Id > 0).ToQueryString();

    [Benchmark]
    public string Postgres_ForUpdate() => _pg.Items.Where(x => x.Id > 0).ForUpdate().ToQueryString();

    [Benchmark]
    public string Postgres_ForUpdate_WithTimeout() =>
        _pg.Items.Where(x => x.Id > 0).ForUpdate(timeout: TimeSpan.FromSeconds(5)).ToQueryString();

    [Benchmark]
    public string Postgres_ForUpdate_MultipleTags() =>
        _pg.Items.Where(x => x.Id > 0).TagWith("custom-tag-a").TagWith("custom-tag-b").ForUpdate().ToQueryString();

    [Benchmark]
    public string MySql_NoLock() => _my.Items.Where(x => x.Id > 0).ToQueryString();

    [Benchmark]
    public string MySql_ForUpdate() => _my.Items.Where(x => x.Id > 0).ForUpdate().ToQueryString();

    [Benchmark]
    public string MySql_ForUpdate_MultipleTags() =>
        _my.Items.Where(x => x.Id > 0).TagWith("custom-tag-a").TagWith("custom-tag-b").ForUpdate().ToQueryString();

    [Benchmark]
    public string SqlServer_NoLock() => _ms.Items.Where(x => x.Id > 0).ToQueryString();

    [Benchmark]
    public string SqlServer_ForUpdate() => _ms.Items.Where(x => x.Id > 0).ForUpdate().ToQueryString();

    [Benchmark]
    public string SqlServer_ForUpdate_MultipleTags() =>
        _ms.Items.Where(x => x.Id > 0).TagWith("custom-tag-a").TagWith("custom-tag-b").ForUpdate().ToQueryString();

    [GlobalCleanup]
    public void Cleanup()
    {
        _pg.Dispose();
        _my.Dispose();
        _ms.Dispose();
    }
}
