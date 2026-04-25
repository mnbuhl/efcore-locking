using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Oracle.Tests;

/// <summary>
/// Oracle rejects SELECT ... FOR UPDATE on query shapes that include collection joins
/// (LEFT JOIN for Include) or analytic functions / ROW_NUMBER (pagination via Skip/Take),
/// raising ORA-02014. The OracleExceptionTranslator maps this to LockingConfigurationException.
/// These overrides replace the positive-path assertions in the shared base with Oracle-specific
/// limitation assertions.
/// </summary>
public partial class IntegrationTests
{
    public override async Task ForUpdate_WithIncludeCollection_LoadsOrderLines()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);
        ctx.OrderLines.Add(
            new OrderLine
            {
                ProductId = id,
                Quantity = 1,
                UnitPrice = 1m,
            }
        );
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () =>
            await ctx.Products.Include(p => p.OrderLines).Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    public override async Task ForUpdate_WithMultipleIncludes_LocksRootTable()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () =>
            await ctx
                .Products.Include(p => p.Category)
                .Include(p => p.OrderLines)
                .Where(p => p.Id == id)
                .ForUpdate()
                .FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    public override async Task ForUpdate_WithOrderByAndTake_LocksPage()
    {
        await using var ctx = CreateContext();
        await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () => await ctx.Products.OrderBy(p => p.Price).Take(2).ForUpdate().ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    public override async Task ForUpdate_WithSkipAndTake_LocksCorrectPage()
    {
        await using var ctx = CreateContext();
        await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () => await ctx.Products.OrderBy(p => p.Price).Skip(2).Take(2).ForUpdate().ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }
}
