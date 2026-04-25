using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

public partial class IntegrationTests
{
    private static readonly Func<TestDbContext, int, Task<Product?>> _compiledForUpdate = EF.CompileAsyncQuery(
        (TestDbContext ctx, int id) =>
            ctx.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.Wait, null).FirstOrDefault()
    );

    private static readonly Func<TestDbContext, int, Task<Product?>> _compiledPlain = EF.CompileAsyncQuery(
        (TestDbContext ctx, int id) => ctx.Products.Where(p => p.Id == id).FirstOrDefault()
    );

    [Fact]
    public async Task ForUpdate_WhenCompiledQuery_WhenInsideTransaction_ShouldExecuteWithLock()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            var (_, id) = await SeedAsync(ctx);

            await using var tx = await ctx.Database.BeginTransactionAsync();
            var product = await _compiledForUpdate(ctx, id);

            product.Should().NotBeNull();
            product!.Id.Should().Be(id);
            cap.LastCommand.Should().Contain("__efcore_locking");

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task ForUpdate_WhenCompiledQuery_WhenWithoutTransaction_ShouldThrowLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await SeedAsync(ctx);

        Func<Task> act = async () => await _compiledForUpdate(ctx, 1);
        await act.Should().ThrowAsync<LockingConfigurationException>();
    }

    [Fact]
    public async Task ForUpdate_WhenCompiledQueryExecutedTwice_ShouldNotLeakLockBetweenExecutions()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            var (_, id) = await SeedAsync(ctx);

            await using var tx = await ctx.Database.BeginTransactionAsync();

            await _compiledForUpdate(ctx, id);
            var lockedIdx = cap.Commands.Count - 1;

            await _compiledPlain(ctx, id);
            var plainIdx = cap.Commands.Count - 1;

            cap.Commands[lockedIdx].Should().Contain("__efcore_locking");
            cap.Commands[plainIdx].Should().NotContain("__efcore_locking");

            await tx.RollbackAsync();
        }
    }
}
