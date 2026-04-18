using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.MySql;
using MySqlConnector;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        Assert.IsType<DeadlockException>(result);
        Assert.Same(ex, result!.InnerException);
    }

    [Fact]
    public void Translate_LockTimeout_ReturnsLockTimeoutException()
    {
        var ex = CreateMySqlException(1205);
        var result = _translator.Translate(ex);
        Assert.IsType<LockTimeoutException>(result);
        Assert.Same(ex, result!.InnerException);
    }

    [Fact]
    public void Translate_UnrelatedError_ReturnsNull()
    {
        Assert.Null(_translator.Translate(CreateMySqlException(1062)));
    }

    [Fact]
    public void Translate_NonMySqlException_ReturnsNull()
    {
        Assert.Null(_translator.Translate(new Exception("random error")));
    }

    [Fact]
    public void Translate_InnerMySqlException_Deadlock_ReturnsDeadlockException()
    {
        var inner = CreateMySqlException(1213);
        var wrapper = new Exception("wrapper", inner);
        var result = _translator.Translate(wrapper);
        Assert.IsType<DeadlockException>(result);
    }

    private static MySqlException CreateMySqlException(int number)
    {
        var ex = (MySqlException)RuntimeHelpers.GetUninitializedObject(typeof(MySqlException));

        // Try the compiler-generated backing field first
        var field = typeof(MySqlException).GetField(
            "<Number>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field is null)
        {
            field = typeof(MySqlException)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name.Contains("Number", StringComparison.OrdinalIgnoreCase)
                                  || f.Name.Contains("number", StringComparison.OrdinalIgnoreCase));
        }

        if (field is null)
            throw new InvalidOperationException(
                $"Cannot locate Number backing field on MySqlException. " +
                $"Available: {string.Join(", ", typeof(MySqlException).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name))}");

        field.SetValue(ex, number);
        return ex;
    }
}
