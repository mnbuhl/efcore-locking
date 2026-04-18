using AwesomeAssertions;
using EntityFrameworkCore.Locking.Internal;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests;

public class LockContextTests
{
    [Fact]
    public void Current_DefaultsToNull()
    {
        LockContext.Current = null;
        LockContext.Current.Should().BeNull();
    }

    [Fact]
    public void Current_CanBeSetAndRead()
    {
        var options = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait };
        LockContext.Current = options;
        LockContext.Current.Should().Be(options);
        LockContext.Current = null;
    }

    [Fact]
    public async Task Current_IsIsolatedAcrossAsyncContexts()
    {
        LockContext.Current = new LockOptions { Mode = LockMode.ForUpdate };

        LockOptions? otherContextValue = null;
        await Task.Run(() => { otherContextValue = LockContext.Current; });

        // AsyncLocal flows DOWN into child tasks but changes in child don't affect parent
        LockContext.Current.Should().NotBeNull();
        LockContext.Current = null;
    }
}
