using AwesomeAssertions;
using EntityFrameworkCore.Locking.Internal;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests;

public class LockTagConstantsTests
{
    [Theory]
    [InlineData("__efcore_locking:ForUpdate:Wait:", LockMode.ForUpdate, LockBehavior.Wait, null)]
    [InlineData("__efcore_locking:ForShare:SkipLocked:", LockMode.ForShare, LockBehavior.SkipLocked, null)]
    [InlineData("__efcore_locking:ForUpdate:NoWait:", LockMode.ForUpdate, LockBehavior.NoWait, null)]
    [InlineData("__efcore_locking:ForUpdate:Wait:500", LockMode.ForUpdate, LockBehavior.Wait, 500.0)]
    [InlineData("__efcore_locking:ForUpdate:Wait:1000.5", LockMode.ForUpdate, LockBehavior.Wait, 1000.5)]
    public void TryParse_ValidTag_ReturnsTrue(string tag, LockMode mode, LockBehavior behavior, double? timeoutMs)
    {
        var result = LockTagConstants.TryParse(tag, out var options);
        result.Should().BeTrue();
        options.Should().NotBeNull();
        options.Mode.Should().Be(mode);
        options.Behavior.Should().Be(behavior);
        if (timeoutMs.HasValue)
            options.Timeout.Should().Be(TimeSpan.FromMilliseconds(timeoutMs.Value));
        else
            options.Timeout.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not_a_lock_tag")]
    [InlineData("__efcore_locking:")]
    [InlineData("__efcore_locking:ForUpdate")]
    [InlineData("__efcore_locking:ForUpdate:Wait")]
    [InlineData("__efcore_locking:InvalidMode:Wait:")]
    [InlineData("__efcore_locking:ForUpdate:Wait:NaN")]
    [InlineData("__efcore_locking:ForUpdate:Wait:Infinity")]
    [InlineData("__efcore_locking:ForUpdate:Wait:-1")]
    [InlineData("__efcore_locking:ForUpdate:Wait:notanumber")]
    public void TryParse_InvalidTag_ReturnsFalse(string tag)
    {
        var result = LockTagConstants.TryParse(tag, out var options);
        result.Should().BeFalse();
        options.Should().BeNull();
    }

    [Fact]
    public void BuildTag_ThenParse_RoundTrips()
    {
        var original = new LockOptions
        {
            Mode = LockMode.ForUpdate,
            Behavior = LockBehavior.Wait,
            Timeout = TimeSpan.FromMilliseconds(250),
        };
        var tag = LockTagConstants.BuildTag(original);
        LockTagConstants.TryParse(tag, out var parsed).Should().BeTrue();
        parsed!.Mode.Should().Be(original.Mode);
        parsed.Behavior.Should().Be(original.Behavior);
        parsed.Timeout.Should().Be(original.Timeout);
    }
}
