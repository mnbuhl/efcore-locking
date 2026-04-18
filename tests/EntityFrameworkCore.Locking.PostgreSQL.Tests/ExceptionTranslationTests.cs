using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL;
using Npgsql;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

public class ExceptionTranslationTests
{
    private readonly PostgresExceptionTranslator _translator = new();

    [Fact]
    public void Translate_Deadlock_ReturnsDeadlockException()
    {
        var pgEx = CreatePostgresException("40P01");
        var result = _translator.Translate(pgEx);
        Assert.IsType<DeadlockException>(result);
        Assert.Same(pgEx, result!.InnerException);
    }

    [Fact]
    public void Translate_LockNotAvailable_ReturnsLockTimeoutException()
    {
        var pgEx = CreatePostgresException("55P03");
        var result = _translator.Translate(pgEx);
        Assert.IsType<LockTimeoutException>(result);
        Assert.Same(pgEx, result!.InnerException);
    }

    [Fact]
    public void Translate_UnrelatedError_ReturnsNull()
    {
        var pgEx = CreatePostgresException("23505");
        Assert.Null(_translator.Translate(pgEx));
    }

    [Fact]
    public void Translate_NonPostgresException_ReturnsNull()
    {
        Assert.Null(_translator.Translate(new Exception("random error")));
    }

    [Fact]
    public void Translate_InnerPostgresException_Deadlock_ReturnsDeadlockException()
    {
        var pgEx = CreatePostgresException("40P01");
        var wrapper = new Exception("wrapper", pgEx);
        var result = _translator.Translate(wrapper);
        Assert.IsType<DeadlockException>(result);
    }

    private static PostgresException CreatePostgresException(string sqlState)
    {
        // PostgresException has no public constructor; use uninitialized object + field injection
        var ex = (PostgresException)RuntimeHelpers.GetUninitializedObject(typeof(PostgresException));

        // SqlState is an auto-property; its backing field is "<SqlState>k__BackingField"
        var field = typeof(PostgresException).GetField(
            "<SqlState>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field is null)
        {
            // Fallback: search for any field whose name contains "SqlState" case-insensitively
            field = typeof(PostgresException)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name.Contains("SqlState", StringComparison.OrdinalIgnoreCase)
                                  || f.Name.Contains("sqlState", StringComparison.OrdinalIgnoreCase));
        }

        if (field is null)
            throw new InvalidOperationException(
                $"Cannot locate SqlState backing field on PostgresException. " +
                $"Available fields: {string.Join(", ", typeof(PostgresException).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name))}");

        field.SetValue(ex, sqlState);
        return ex;
    }
}
