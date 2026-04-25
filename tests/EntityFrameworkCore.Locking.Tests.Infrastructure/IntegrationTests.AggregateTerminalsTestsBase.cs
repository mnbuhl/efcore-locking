using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

public abstract partial class IntegrationTestsBase
{
    [Fact]
    public async Task ForUpdate_ThenCountAsync_WhenCondition_ShouldThrowLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () => await ctx.Products.Where(p => p.Id == id).ForUpdate().CountAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ThenLongCountAsync_WhenCondition_ShouldThrowLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () => await ctx.Products.Where(p => p.Id == id).ForUpdate().LongCountAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ThenSumAsync_WhenCondition_ShouldThrowLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await SeedAsync(ctx, price: 9.99m);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () => await ctx.Products.ForUpdate().SumAsync(p => p.Price);

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ThenMaxAsync_WhenCondition_ShouldThrowLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await SeedAsync(ctx, price: 9.99m);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () => await ctx.Products.ForUpdate().MaxAsync(p => p.Price);

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ThenMinAsync_WhenCondition_ShouldThrowLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await SeedAsync(ctx, price: 9.99m);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        Func<Task> act = async () => await ctx.Products.ForUpdate().MinAsync(p => p.Price);

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ThenAnyAsync_WhenRowExists_ShouldReturnTrueWithoutThrowing()
    {
        // AnyAsync is safe: EF Core translates it to a scalar subquery without an outer aggregate
        // function — the guard must NOT fire.
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var exists = await ctx.Products.Where(p => p.Id == id).ForUpdate().AnyAsync();

        exists.Should().BeTrue();
        await tx.RollbackAsync();
    }
}
