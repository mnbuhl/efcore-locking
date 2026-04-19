using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.SqlServer;
using Microsoft.Data.SqlClient;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

public class ExceptionTranslationTests
{
    private readonly SqlServerExceptionTranslator _translator = new();

    [Fact]
    public void Translate_Deadlock_ReturnsDeadlockException()
    {
        var ex = CreateSqlException(1205);
        var result = _translator.Translate(ex);
        result.Should().BeOfType<DeadlockException>();
        result!.InnerException.Should().BeSameAs(ex);
    }

    [Fact]
    public void Translate_LockTimeout_ReturnsLockTimeoutException()
    {
        var ex = CreateSqlException(1222);
        var result = _translator.Translate(ex);
        result.Should().BeOfType<LockTimeoutException>();
        result!.InnerException.Should().BeSameAs(ex);
    }

    [Fact]
    public void Translate_UnrelatedError_ReturnsNull()
    {
        _translator.Translate(CreateSqlException(2627)).Should().BeNull();
    }

    [Fact]
    public void Translate_NonSqlException_ReturnsNull()
    {
        _translator.Translate(new Exception("random error")).Should().BeNull();
    }

    [Fact]
    public void Translate_InnerSqlException_Deadlock_ReturnsDeadlockException()
    {
        var inner = CreateSqlException(1205);
        var wrapper = new Exception("wrapper", inner);
        _translator.Translate(wrapper).Should().BeOfType<DeadlockException>();
    }

    internal static SqlException CreateSqlException(int number)
    {
        var sqlClientAsm = typeof(SqlException).Assembly;
        var collectionType = sqlClientAsm.GetType("Microsoft.Data.SqlClient.SqlErrorCollection")!;
        var errorType = sqlClientAsm.GetType("Microsoft.Data.SqlClient.SqlError")!;

        var error = RuntimeHelpers.GetUninitializedObject(errorType);
        errorType
            .GetField("_number", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(error, number);

        var collection = RuntimeHelpers.GetUninitializedObject(collectionType);

        var listField = collectionType
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f =>
                f.FieldType.IsGenericType
                && f.FieldType.GetGenericTypeDefinition() == typeof(List<>)
            );
        if (listField is not null)
        {
            var listInstance = Activator.CreateInstance(listField.FieldType)!;
            listField.SetValue(collection, listInstance);
        }

        collectionType
            .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(collection, [error]);

        var createMethod = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.NonPublic | BindingFlags.Static,
            [collectionType, typeof(string)]
        )!;

        return (SqlException)createMethod.Invoke(null, [collection, "15.0"])!;
    }
}
