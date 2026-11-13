using System.Data;
using System.Data.Common;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests;

public class LockTagInterceptorTests
{
    private const string TaggedSql = "-- custom\r\n-- __efcore_locking:ForUpdate:Wait:500\r\nSELECT 1";
    private static readonly EventDefinition Definition = new(
        new LoggingOptions(),
        new EventId(1),
        LogLevel.Debug,
        "TestCommand",
        level => LoggerMessage.Define(level, new EventId(1), "Test command")
    );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Executing_LeadingTag_StillRequiresTransaction(bool async)
    {
        using var connection = new SqlConnection();
        using var command = connection.CreateCommand();
        command.CommandText = TaggedSql;
        var interceptor = new LockingValidationInterceptor();
        var eventData = ExecutingEvent(command, null, async);

        if (async)
            await Assert.ThrowsAsync<LockingConfigurationException>(async () =>
                await interceptor.ReaderExecutingAsync(command, eventData, default)
            );
        else
            Assert.Throws<LockingConfigurationException>(() =>
                interceptor.ReaderExecuting(command, eventData, default)
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Executing_MarkerInSqlLiteral_DoesNotRequireTransaction(bool async)
    {
        using var connection = new SqlConnection();
        using var command = connection.CreateCommand();
        // Keep the rest of this line parseable by the old full-text scan.
        command.CommandText = "SELECT '__efcore_locking:ForUpdate:Wait:\n'";
        var interceptor = new LockingValidationInterceptor();
        var eventData = ExecutingEvent(command, null, async);

        if (async)
            await interceptor.ReaderExecutingAsync(command, eventData, default);
        else
            interceptor.ReaderExecuting(command, eventData, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SET LOCAL lock_timeout = '500ms';\n")]
    [InlineData("SET SESSION innodb_lock_wait_timeout = 1;\n")]
    [InlineData("SET LOCK_TIMEOUT 500;\n")]
    [InlineData("SET LOCK_TIMEOUT 0;\n")]
    public async Task Executed_LeadingTag_WrapsReaderAndClearsContext(string preamble)
    {
        using var context = new TranslatorContext();
        using var connection = new SqlConnection();
        using var command = connection.CreateCommand();
        command.CommandText = preamble + TaggedSql;
        using var reader = new DataTable().CreateDataReader();
        var interceptor = new LockingValidationInterceptor();

        foreach (var async in new[] { false, true })
        {
            LockContext.Current = new LockOptions();
            var eventData = ExecutedEvent(command, context, reader, async);
            var result = async
                ? await interceptor.ReaderExecutedAsync(command, eventData, reader)
                : interceptor.ReaderExecuted(command, eventData, reader);
            result.Should().BeOfType<TranslatingDbDataReader>();
            LockContext.Current.Should().BeNull();
        }
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT '__efcore_locking:ForUpdate:Wait:'")]
    public async Task Executed_UntaggedSql_DoesNotWrapReaderDespiteStaleContext(string sql)
    {
        using var context = new TranslatorContext();
        using var connection = new SqlConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = new DataTable().CreateDataReader();
        var interceptor = new LockingValidationInterceptor();

        foreach (var async in new[] { false, true })
        {
            LockContext.Current = new LockOptions();
            var eventData = ExecutedEvent(command, context, reader, async);
            var result = async
                ? await interceptor.ReaderExecutedAsync(command, eventData, reader)
                : interceptor.ReaderExecuted(command, eventData, reader);
            result.Should().BeSameAs(reader);
            LockContext.Current.Should().BeNull();
        }
    }

    private static CommandEventData ExecutingEvent(DbCommand command, DbContext? context, bool async) =>
        new(
            Definition,
            (_, _) => "Test command",
            command.Connection!,
            command,
#if NET10_0_OR_GREATER
            command.CommandText,
#endif
            context,
            DbCommandMethod.ExecuteReader,
            Guid.NewGuid(),
            Guid.NewGuid(),
            async,
            false,
            DateTimeOffset.UtcNow,
            CommandSource.LinqQuery
        );

    private static CommandExecutedEventData ExecutedEvent(
        DbCommand command,
        DbContext context,
        DbDataReader reader,
        bool async
    ) =>
        new(
            Definition,
            (_, _) => "Test command",
            command.Connection!,
            command,
#if NET10_0_OR_GREATER
            command.CommandText,
#endif
            context,
            DbCommandMethod.ExecuteReader,
            Guid.NewGuid(),
            Guid.NewGuid(),
            reader,
            async,
            false,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            CommandSource.LinqQuery
        );

    private sealed class TranslatorContext : DbContext, IInfrastructure<IServiceProvider>
    {
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddSingleton<IExceptionTranslator, NoopTranslator>()
            .BuildServiceProvider();

        IServiceProvider IInfrastructure<IServiceProvider>.Instance => _services;

        public override void Dispose()
        {
            _services.Dispose();
            base.Dispose();
        }
    }

    private sealed class NoopTranslator : IExceptionTranslator
    {
        public LockingException? Translate(Exception exception) => null;
    }
}
