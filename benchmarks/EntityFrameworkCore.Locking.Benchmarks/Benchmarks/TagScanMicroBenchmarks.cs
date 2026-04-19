using BenchmarkDotNet.Attributes;
using EntityFrameworkCore.Locking.Internal;

namespace EntityFrameworkCore.Locking.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class TagScanMicroBenchmarks
{
    private HashSet<string> _tagsEmpty = null!;
    private HashSet<string> _tagsWithLock = null!;
    private HashSet<string> _tagsWithLockPlusTwo = null!;
    private string _expectedTag = null!;

    [GlobalSetup]
    public void Setup()
    {
        _expectedTag = LockTagConstants.BuildTag(
            new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait }
        );
        _tagsEmpty = [];
        _tagsWithLock = [_expectedTag];
        _tagsWithLockPlusTwo = ["custom-tag-a", _expectedTag, "custom-tag-b"];
    }

    [Benchmark(Baseline = true)]
    public bool Old_StartsWith_Empty() =>
        _tagsEmpty.Any(t => t.StartsWith(LockTagConstants.Prefix, StringComparison.Ordinal));

    [Benchmark]
    public bool New_Contains_Empty() => _tagsEmpty.Contains(_expectedTag);

    [Benchmark]
    public bool Old_StartsWith_Single() =>
        _tagsWithLock.Any(t => t.StartsWith(LockTagConstants.Prefix, StringComparison.Ordinal));

    [Benchmark]
    public bool New_Contains_Single() => _tagsWithLock.Contains(_expectedTag);

    [Benchmark]
    public bool Old_StartsWith_Multi() =>
        _tagsWithLockPlusTwo.Any(t => t.StartsWith(LockTagConstants.Prefix, StringComparison.Ordinal));

    [Benchmark]
    public bool New_Contains_Multi() => _tagsWithLockPlusTwo.Contains(_expectedTag);
}
