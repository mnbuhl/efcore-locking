using System.Data;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public class ConcurrencyTests(SqlServerFixture fixture) : ConcurrencyTestsBase
{
    // SqlServer honors sub-second LOCK_TIMEOUT; preserve original 500ms behavior.
    protected override TimeSpan WaitTimeout => TimeSpan.FromMilliseconds(500);

    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlServer(fixture.ConnectionString).UseLocking().Options);

    protected override async Task ResetDatabaseAsync(TestDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [OrderLines]");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [Products]");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [Categories]");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[OrderLines]', RESEED, 0)");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[Products]', RESEED, 0)");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[Categories]', RESEED, 0)");
    }

    // --- SqlServer-specific ---

    [Theory]
    [InlineData(false, IsolationLevel.ReadCommitted, false)]
    [InlineData(false, IsolationLevel.ReadCommitted, true)]
    [InlineData(true, IsolationLevel.ReadCommitted, false)]
    [InlineData(true, IsolationLevel.ReadCommitted, true)]
    [InlineData(false, IsolationLevel.Snapshot, false)]
    [InlineData(false, IsolationLevel.Snapshot, true)]
    public async Task ForShare_AllowsReaders_BlocksWriters_UntilTransactionEnds(
        bool rcsi,
        IsolationLevel isolation,
        bool commit
    )
    {
        // Isolate database options from the fixture's other tests and pooled connections.
        var connection = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = $"ForShare_{Guid.NewGuid():N}",
            Pooling = false,
        };
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .UseLocking()
            .Options;
        await using var setup = new TestDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        try
        {
            await setup.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON");
            if (rcsi)
                await setup.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON");
            var id = await SeedAsync(setup);

            await using var reader = new TestDbContext(options);
            await using var tx = await reader.Database.BeginTransactionAsync(isolation);
            (await reader.Products.Where(p => p.Id == id).ForShare().FirstAsync()).Should().NotBeNull();

            await using (var otherReader = new TestDbContext(options))
            {
                await using var otherTx = await otherReader.Database.BeginTransactionAsync(isolation);
                (await otherReader.Products.Where(p => p.Id == id).ForShare(LockBehavior.NoWait).FirstAsync())
                    .Should()
                    .NotBeNull();
                await otherTx.RollbackAsync();
            }

            await using var writer = new TestDbContext(options);
            await writer.Database.OpenConnectionAsync();
            await writer.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 500");
            Func<Task> update = async () =>
                await writer.Products.Where(p => p.Id == id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Stock, 5));
            Func<Task> delete = async () => await writer.Products.Where(p => p.Id == id).ExecuteDeleteAsync();
            await update.Should().ThrowAsync<LockTimeoutException>();
            await delete.Should().ThrowAsync<LockTimeoutException>();

            if (commit)
                await tx.CommitAsync();
            else
                await tx.RollbackAsync();

            await update.Should().NotThrowAsync();
            await delete.Should().NotThrowAsync();
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData(LockBehavior.NoWait)]
    [InlineData(LockBehavior.Wait)]
    public async Task ForShare_ContendedRow_ThrowsLockTimeoutException(LockBehavior behavior)
    {
        await using var writer = CreateContext();
        var id = await SeedAsync(writer);
        await using var writerTx = await writer.Database.BeginTransactionAsync();
        await writer.Products.Where(p => p.Id == id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Stock, 5));

        await using var reader = CreateContext();
        await using var readerTx = await reader.Database.BeginTransactionAsync();
        Func<Task> read = async () =>
            await reader
                .Products.Where(p => p.Id == id)
                .ForShare(behavior, behavior == LockBehavior.Wait ? WaitTimeout : null)
                .FirstAsync();
        await read.Should().ThrowAsync<LockTimeoutException>();
    }
}
