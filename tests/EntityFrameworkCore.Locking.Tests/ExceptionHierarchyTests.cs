using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests;

public class ExceptionHierarchyTests
{
    [Fact]
    public void LockTimeoutException_IsLockAcquisitionFailedException() =>
        new LockTimeoutException("msg").Should().BeAssignableTo<LockAcquisitionFailedException>();

    [Fact]
    public void DeadlockException_IsLockAcquisitionFailedException() =>
        new DeadlockException("msg").Should().BeAssignableTo<LockAcquisitionFailedException>();

    [Fact]
    public void LockAcquisitionFailedException_IsLockingException() =>
        new LockAcquisitionFailedException("msg").Should().BeAssignableTo<LockingException>();

    [Fact]
    public void LockingConfigurationException_IsLockingException() =>
        new LockingConfigurationException("msg").Should().BeAssignableTo<LockingException>();

    [Fact]
    public void AllExceptions_PreserveInnerException()
    {
        var inner = new Exception("inner");
        new LockTimeoutException("msg", inner).InnerException.Should().BeSameAs(inner);
        new DeadlockException("msg", inner).InnerException.Should().BeSameAs(inner);
        new LockingConfigurationException("msg", inner).InnerException.Should().BeSameAs(inner);
    }
}
