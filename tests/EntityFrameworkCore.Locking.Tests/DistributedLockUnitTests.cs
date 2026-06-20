using System.Data;
using System.Data.Common;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests;

public class DistributedLockUnitTests
{
    // --- LockAlreadyHeldException ---

    [Fact]
    public void LockAlreadyHeldException_Key_IsPreserved()
    {
        var ex = new LockAlreadyHeldException("my-key");
        ex.Key.Should().Be("my-key");
        ex.Message.Should().Contain("my-key");
    }

    [Fact]
    public void LockAlreadyHeldException_InheritsLockingException()
    {
        var ex = new LockAlreadyHeldException("k");
        ex.Should().BeAssignableTo<LockingException>();
    }

    // --- Key validation ---

    [Fact]
    public async Task AcquireDistributedLockAsync_NullKey_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await Assert.ThrowsAsync<LockingConfigurationException>(() => ctx.Database.AcquireDistributedLockAsync(null!));
    }

    [Fact]
    public async Task AcquireDistributedLockAsync_EmptyKey_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await Assert.ThrowsAsync<LockingConfigurationException>(() => ctx.Database.AcquireDistributedLockAsync(""));
    }

    [Fact]
    public async Task AcquireDistributedLockAsync_KeyTooLong_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        var longKey = new string('a', 256);
        await Assert.ThrowsAsync<LockingConfigurationException>(() =>
            ctx.Database.AcquireDistributedLockAsync(longKey)
        );
    }

    [Fact]
    public async Task AcquireDistributedLockAsync_InvalidMode_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();

        await Assert.ThrowsAsync<LockingConfigurationException>(() =>
            ctx.Database.AcquireDistributedLockAsync("invalid-mode", mode: (DistributedLockMode)999)
        );
    }

    [Fact]
    public void AcquireDistributedLock_InvalidMode_ThrowsLockingConfigurationException()
    {
        using var ctx = CreateContext();

        Assert.Throws<LockingConfigurationException>(() =>
            ctx.Database.AcquireDistributedLock("invalid-sync-mode", mode: (DistributedLockMode)999)
        );
    }

    [Fact]
    public async Task AcquireDistributedLockAsync_MaxKey255_Accepted()
    {
        await using var ctx = CreateContext();
        var key = new string('a', 255);
        await using var handle = await ctx.Database.AcquireDistributedLockAsync(key);
        handle.Should().NotBeNull();
        handle.Key.Should().Be(key);
    }

    // --- SupportsDistributedLocks ---

    [Fact]
    public void SupportsDistributedLocks_WithFakeProvider_ReturnsTrue()
    {
        using var ctx = CreateContext();
        ctx.Database.SupportsDistributedLocks().Should().BeTrue();
    }

    // --- Handle idempotence ---

    [Fact]
    public async Task ReleaseAsync_CalledTwice_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var handle = await ctx.Database.AcquireDistributedLockAsync("key");
        await handle.ReleaseAsync();
        // Second release should not throw
        await handle.ReleaseAsync();
    }

    [Fact]
    public async Task DisposeAfterRelease_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var handle = await ctx.Database.AcquireDistributedLockAsync("key");
        await handle.ReleaseAsync();
        await handle.DisposeAsync(); // no throw
    }

    // --- Registry: LockAlreadyHeldException on double acquire ---

    [Fact]
    public async Task DoubleAcquire_SameKey_ThrowsLockAlreadyHeld()
    {
        await using var ctx = CreateContext();
        await using var h1 = await ctx.Database.AcquireDistributedLockAsync("dup-key");

        var ex = await Assert.ThrowsAsync<LockAlreadyHeldException>(() =>
            ctx.Database.AcquireDistributedLockAsync("dup-key")
        );
        ex.Key.Should().Be("dup-key");
    }

    [Fact]
    public async Task AfterRelease_SameKey_CanBeAcquiredAgain()
    {
        await using var ctx = CreateContext();
        var h1 = await ctx.Database.AcquireDistributedLockAsync("reuse-key");
        await h1.ReleaseAsync();

        await using var h2 = await ctx.Database.AcquireDistributedLockAsync("reuse-key");
        h2.Should().NotBeNull();
    }

    // --- TryAcquire returns handle on free key ---

    [Fact]
    public async Task TryAcquireDistributedLockAsync_FreeKey_ReturnsHandle()
    {
        await using var ctx = CreateContext();
        var handle = await ctx.Database.TryAcquireDistributedLockAsync("free");
        handle.Should().NotBeNull();
        ctx.LockingProvider.Advisory.LastMode.Should().Be(DistributedLockMode.Exclusive);
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task AcquireDistributedLockAsync_DefaultMode_UsesExclusive()
    {
        await using var ctx = CreateContext();

        await using var handle = await ctx.Database.AcquireDistributedLockAsync("default-mode");

        handle.Should().NotBeNull();
        ctx.LockingProvider.Advisory.LastMode.Should().Be(DistributedLockMode.Exclusive);
    }

    [Fact]
    public async Task AcquireDistributedLockAsync_ExplicitSharedMode_IsPassedToProvider()
    {
        await using var ctx = CreateContext();

        await using var handle = await ctx.Database.AcquireDistributedLockAsync(
            "shared-mode",
            mode: DistributedLockMode.Shared
        );

        handle.Should().NotBeNull();
        ctx.LockingProvider.Advisory.LastMode.Should().Be(DistributedLockMode.Shared);
    }

    [Fact]
    public async Task TryAcquireDistributedLockAsync_ExplicitSharedMode_IsPassedToProvider()
    {
        await using var ctx = CreateContext();

        await using var handle = await ctx.Database.TryAcquireDistributedLockAsync(
            "try-shared-mode",
            mode: DistributedLockMode.Shared
        );

        handle.Should().NotBeNull();
        ctx.LockingProvider.Advisory.LastMode.Should().Be(DistributedLockMode.Shared);
    }

    [Fact]
    public async Task AcquireDistributedLockAsync_ProviderRejectedMode_DoesNotOpenConnection()
    {
        var fakeConn = new FakeDbConnection(ConnectionState.Closed, throwOnOpen: true);
        var fakeProvider = new FakeLockingProvider(new FakeAdvisoryLockProvider(rejectShared: true));
        await using var ctx = CreateContext(fakeConn, fakeProvider);

        await Assert.ThrowsAsync<LockingConfigurationException>(() =>
            ctx.Database.AcquireDistributedLockAsync("provider-rejected-mode", mode: DistributedLockMode.Shared)
        );

        fakeConn.OpenCount.Should().Be(0);
    }

    [Fact]
    public void AcquireDistributedLock_ExplicitSharedMode_IsPassedToProvider()
    {
        using var ctx = CreateContext();

        using var handle = ctx.Database.AcquireDistributedLock(
            "sync-shared-mode",
            mode: DistributedLockMode.Shared
        );

        handle.Should().NotBeNull();
        ctx.LockingProvider.Advisory.LastMode.Should().Be(DistributedLockMode.Shared);
    }

    [Fact]
    public void TryAcquireDistributedLock_ExplicitSharedMode_IsPassedToProvider()
    {
        using var ctx = CreateContext();

        using var handle = ctx.Database.TryAcquireDistributedLock(
            "try-sync-shared-mode",
            mode: DistributedLockMode.Shared
        );

        handle.Should().NotBeNull();
        ctx.LockingProvider.Advisory.LastMode.Should().Be(DistributedLockMode.Shared);
    }

    // --- Factory ---

    private static FakeDbContext CreateContext(
        FakeDbConnection? fakeConn = null,
        FakeLockingProvider? fakeProvider = null
    )
    {
        fakeConn ??= new FakeDbConnection();
        fakeProvider ??= new FakeLockingProvider();

        var options = new DbContextOptionsBuilder<FakeDbContext>()
            .UseSqlServer(fakeConn)
            .EnableServiceProviderCaching(false)
            .Options;

        // Inject the fake locking provider via the options extension
        var extension = new LockingOptionsExtension(fakeProvider);
        options = (DbContextOptions<FakeDbContext>)options.WithExtension(extension);

        return new FakeDbContext(options, fakeProvider);
    }
}

// --- Minimal DbContext that exposes the fake connection ---

internal sealed class FakeDbContext : DbContext
{
    public FakeDbContext(DbContextOptions<FakeDbContext> options, FakeLockingProvider lockingProvider)
        : base(options)
    {
        LockingProvider = lockingProvider;
    }

    public FakeLockingProvider LockingProvider { get; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }
}

// --- Always-open fake DbConnection (no real SQL server needed) ---

internal sealed class FakeDbConnection : DbConnection
{
    private readonly bool _throwOnOpen;
    private ConnectionState _state;

    public FakeDbConnection(ConnectionState state = ConnectionState.Open, bool throwOnOpen = false)
    {
        _state = state;
        _throwOnOpen = throwOnOpen;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = "Fake";
    public override string Database => "Fake";
    public override string DataSource => "Fake";
    public override string ServerVersion => "0.0";
    public override ConnectionState State => _state;

    public int OpenCount { get; private set; }

    public override void Open()
    {
        OpenCount++;
        if (_throwOnOpen)
            throw new InvalidOperationException("Fake connection should not be opened.");
        _state = ConnectionState.Open;
    }

    public override void Close() => _state = ConnectionState.Closed;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        Open();
        return Task.CompletedTask;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException();

    public override void ChangeDatabase(string databaseName) { }

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
}

// --- Fake ILockingProvider that delegates distributed lock to FakeAdvisoryLockProvider ---

internal sealed class FakeLockingProvider : ILockingProvider
{
    private readonly FakeAdvisoryLockProvider _advisory;

    public FakeLockingProvider(FakeAdvisoryLockProvider? advisory = null)
    {
        _advisory = advisory ?? new FakeAdvisoryLockProvider();
    }

    public FakeAdvisoryLockProvider Advisory => _advisory;
    public ILockSqlGenerator RowLockGenerator { get; } = new FakeLockSqlGenerator();
    public string ProviderName => "Fake";
    public IExceptionTranslator ExceptionTranslator { get; } = new FakeExceptionTranslator();
    public IAdvisoryLockProvider AdvisoryLockProvider => _advisory;
}

internal sealed class FakeLockSqlGenerator : ILockSqlGenerator
{
    public string? GenerateLockClause(LockOptions options) => null;

    public bool SupportsLockOptions(LockOptions options) => false;

    public string? GeneratePreStatementSql(LockOptions options) => null;
}

internal sealed class FakeExceptionTranslator : IExceptionTranslator
{
    public LockingException? Translate(Exception exception) => null;
}

// --- Fake IAdvisoryLockProvider backed by an in-memory set ---

internal sealed class FakeAdvisoryLockProvider : IAdvisoryLockProvider
{
    // Tracks which keys are held per connection (simulates session-scoped locks)
    private readonly Dictionary<DbConnection, HashSet<string>> _held = new();
    private readonly object _gate = new();
    private readonly bool _rejectShared;

    public FakeAdvisoryLockProvider(bool rejectShared = false)
    {
        _rejectShared = rejectShared;
    }

    public DistributedLockMode LastMode { get; private set; }

    public void ValidateMode(DistributedLockMode mode)
    {
        switch (mode)
        {
            case DistributedLockMode.Exclusive:
                return;
            case DistributedLockMode.Shared when !_rejectShared:
                return;
            case DistributedLockMode.Shared:
                throw new LockingConfigurationException(
                    "Shared distributed locks are not supported by the fake provider."
                );
            default:
                throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'.");
        }
    }

    public Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        LastMode = mode;
        var handle = CreateHandle(context, connection, key);
        return Task.FromResult(handle);
    }

    public Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        LastMode = mode;
        IDistributedLockHandle? handle;
        lock (_gate)
        {
            if (_held.TryGetValue(connection, out var keys) && keys.Contains(key))
            {
                handle = null;
            }
            else
            {
                handle = CreateHandle(context, connection, key);
            }
        }
        return Task.FromResult(handle);
    }

    public IDistributedLockHandle Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        LastMode = mode;
        return CreateHandle(context, connection, key);
    }

    public IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        LastMode = mode;
        lock (_gate)
        {
            if (_held.TryGetValue(connection, out var keys) && keys.Contains(key))
                return null;
            return CreateHandle(context, connection, key);
        }
    }

    private IDistributedLockHandle CreateHandle(DbContext context, DbConnection connection, string key)
    {
        lock (_gate)
        {
            if (!_held.TryGetValue(connection, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                _held[connection] = keys;
            }
            keys.Add(key);
        }

        return new DistributedLockHandle(
            key,
            connection,
            openedByConnection: false,
            releaseAsync: _ =>
            {
                Release(connection, key, context);
                return Task.CompletedTask;
            },
            releaseSync: () => Release(connection, key, context)
        );
    }

    private void Release(DbConnection connection, string key, DbContext context)
    {
        lock (_gate)
        {
            if (_held.TryGetValue(connection, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    _held.Remove(connection);
            }
        }
        DistributedLockRegistry.Unregister(context, connection, key);
    }
}
