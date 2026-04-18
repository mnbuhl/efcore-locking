using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.SqlServer;
using Microsoft.Data.SqlClient;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        Assert.IsType<DeadlockException>(result);
        Assert.Same(ex, result!.InnerException);
    }

    [Fact]
    public void Translate_LockTimeout_ReturnsLockTimeoutException()
    {
        var ex = CreateSqlException(1222);
        var result = _translator.Translate(ex);
        Assert.IsType<LockTimeoutException>(result);
        Assert.Same(ex, result!.InnerException);
    }

    [Fact]
    public void Translate_UnrelatedError_ReturnsNull()
    {
        Assert.Null(_translator.Translate(CreateSqlException(2627)));
    }

    [Fact]
    public void Translate_NonSqlException_ReturnsNull()
    {
        Assert.Null(_translator.Translate(new Exception("random error")));
    }

    [Fact]
    public void Translate_InnerSqlException_Deadlock_ReturnsDeadlockException()
    {
        var inner = CreateSqlException(1205);
        var wrapper = new Exception("wrapper", inner);
        Assert.IsType<DeadlockException>(_translator.Translate(wrapper));
    }

    internal static SqlException CreateSqlException(int number)
    {
        var sqlClientAsm = typeof(SqlException).Assembly;
        var collectionType = sqlClientAsm.GetType("Microsoft.Data.SqlClient.SqlErrorCollection")!;
        var errorType = sqlClientAsm.GetType("Microsoft.Data.SqlClient.SqlError")!;

        // Build SqlError via uninitialized object + field injection
        var error = RuntimeHelpers.GetUninitializedObject(errorType);
        errorType.GetField("_number", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(error, number);

        // Build SqlErrorCollection via uninitialized object + Add method
        var collection = RuntimeHelpers.GetUninitializedObject(collectionType);

        // SqlErrorCollection.Add needs the internal List to exist; initialize via a field that holds the list
        var listField = collectionType
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>));
        if (listField is not null)
        {
            var listInstance = Activator.CreateInstance(listField.FieldType)!;
            listField.SetValue(collection, listInstance);
        }

        collectionType.GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(collection, [error]);

        // Create SqlException via internal static factory
        var createMethod = typeof(SqlException)
            .GetMethod("CreateException",
                BindingFlags.NonPublic | BindingFlags.Static,
                [collectionType, typeof(string)])!;

        return (SqlException)createMethod.Invoke(null, [collection, "15.0"])!;
    }
}
