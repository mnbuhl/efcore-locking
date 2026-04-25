using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Npgsql;
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
        result.Should().BeOfType<DeadlockException>();
        result.InnerException.Should().BeSameAs(pgEx);
    }

    [Fact]
    public void Translate_LockNotAvailable_ReturnsLockTimeoutException()
    {
        var pgEx = CreatePostgresException("55P03");
        var result = _translator.Translate(pgEx);
        result.Should().BeOfType<LockTimeoutException>();
        result.InnerException.Should().BeSameAs(pgEx);
    }

    [Fact]
    public void Translate_UnrelatedError_ReturnsNull()
    {
        var pgEx = CreatePostgresException("23505");
        _translator.Translate(pgEx).Should().BeNull();
    }

    [Fact]
    public void Translate_NonPostgresException_ReturnsNull()
    {
        _translator.Translate(new Exception("random error")).Should().BeNull();
    }

    [Fact]
    public void Translate_InnerPostgresException_Deadlock_ReturnsDeadlockException()
    {
        var pgEx = CreatePostgresException("40P01");
        var wrapper = new Exception("wrapper", pgEx);
        _translator.Translate(wrapper).Should().BeOfType<DeadlockException>();
    }

    [Fact]
    public void Translate_QueryCanceled_ReturnsNull()
    {
        // 57014 = query_canceled — not a lock error, should not translate
        _translator.Translate(CreatePostgresException("57014")).Should().BeNull();
    }

    private static PostgresException CreatePostgresException(string sqlState)
    {
        var ex = (PostgresException)RuntimeHelpers.GetUninitializedObject(typeof(PostgresException));

        var field = typeof(PostgresException).GetField(
            "<SqlState>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        if (field is null)
        {
            field = typeof(PostgresException)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f =>
                    f.Name.Contains("SqlState", StringComparison.OrdinalIgnoreCase)
                    || f.Name.Contains("sqlState", StringComparison.OrdinalIgnoreCase)
                );
        }

        if (field is null)
            throw new InvalidOperationException(
                $"Cannot locate SqlState backing field on PostgresException. "
                    + $"Available fields: {string.Join(", ", typeof(PostgresException).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name))}"
            );

        field.SetValue(ex, sqlState);
        return ex;
    }
}
