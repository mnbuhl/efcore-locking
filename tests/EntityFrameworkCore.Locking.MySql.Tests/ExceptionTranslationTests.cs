using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.MySql;
using MySqlConnector;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

public class ExceptionTranslationTests
{
    private readonly MySqlExceptionTranslator _translator = new();

    [Fact]
    public void Translate_Deadlock_ReturnsDeadlockException()
    {
        var ex = CreateMySqlException(1213);
        var result = _translator.Translate(ex);
        result.Should().BeOfType<DeadlockException>();
        result!.InnerException.Should().BeSameAs(ex);
    }

    [Fact]
    public void Translate_LockTimeout_ReturnsLockTimeoutException()
    {
        var ex = CreateMySqlException(1205);
        var result = _translator.Translate(ex);
        result.Should().BeOfType<LockTimeoutException>();
        result!.InnerException.Should().BeSameAs(ex);
    }

    [Fact]
    public void Translate_NoWaitAbort_ReturnsLockTimeoutException()
    {
        // 3572 = "Statement aborted because lock(s) could not be acquired immediately and NOWAIT is set"
        var ex = CreateMySqlException(3572);
        var result = _translator.Translate(ex);
        result.Should().BeOfType<LockTimeoutException>();
        result!.InnerException.Should().BeSameAs(ex);
    }

    [Fact]
    public void Translate_UnrelatedError_ReturnsNull()
    {
        _translator.Translate(CreateMySqlException(1062)).Should().BeNull();
    }

    [Fact]
    public void Translate_NonMySqlException_ReturnsNull()
    {
        _translator.Translate(new Exception("random error")).Should().BeNull();
    }

    [Fact]
    public void Translate_InnerMySqlException_Deadlock_ReturnsDeadlockException()
    {
        var inner = CreateMySqlException(1213);
        var wrapper = new Exception("wrapper", inner);
        _translator.Translate(wrapper).Should().BeOfType<DeadlockException>();
    }

    private static MySqlException CreateMySqlException(int number)
    {
        var ex = (MySqlException)RuntimeHelpers.GetUninitializedObject(typeof(MySqlException));

        var field = typeof(MySqlException).GetField(
            "<Number>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        if (field is null)
        {
            field = typeof(MySqlException)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f =>
                    f.Name.Contains("Number", StringComparison.OrdinalIgnoreCase)
                    || f.Name.Contains("number", StringComparison.OrdinalIgnoreCase)
                );
        }

        if (field is null)
            throw new InvalidOperationException(
                $"Cannot locate Number backing field on MySqlException. "
                    + $"Available: {string.Join(", ", typeof(MySqlException).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name))}"
            );

        field.SetValue(ex, number);
        return ex;
    }
}
