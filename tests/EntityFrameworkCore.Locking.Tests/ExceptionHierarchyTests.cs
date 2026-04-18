using EntityFrameworkCore.Locking.Exceptions;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests;

public class ExceptionHierarchyTests
{
    [Fact]
    public void LockTimeoutException_IsLockAcquisitionFailedException()
        => Assert.IsAssignableFrom<LockAcquisitionFailedException>(new LockTimeoutException("msg"));

    [Fact]
    public void DeadlockException_IsLockAcquisitionFailedException()
        => Assert.IsAssignableFrom<LockAcquisitionFailedException>(new DeadlockException("msg"));

    [Fact]
    public void LockAcquisitionFailedException_IsLockingException()
        => Assert.IsAssignableFrom<LockingException>(new LockAcquisitionFailedException("msg"));

    [Fact]
    public void LockingConfigurationException_IsLockingException()
        => Assert.IsAssignableFrom<LockingException>(new LockingConfigurationException("msg"));

    [Fact]
    public void AllExceptions_PreserveInnerException()
    {
        var inner = new Exception("inner");
        Assert.Same(inner, new LockTimeoutException("msg", inner).InnerException);
        Assert.Same(inner, new DeadlockException("msg", inner).InnerException);
        Assert.Same(inner, new LockingConfigurationException("msg", inner).InnerException);
    }
}
