# Shared Distributed Locks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add shared distributed lock support for providers with native shared advisory locks while keeping exclusive locks as the default mode.

**Architecture:** Introduce a core `DistributedLockMode` enum and pass it from `DatabaseFacade` extension methods through `IAdvisoryLockProvider`. PostgreSQL and SQL Server map the mode to native shared advisory/application locks; MySQL keeps exclusive `GET_LOCK` behavior and rejects shared mode with `LockingConfigurationException`.

**Tech Stack:** C#/.NET 10 solution, EF Core provider extensions, Npgsql, Microsoft.Data.SqlClient, MySqlConnector, xUnit, AwesomeAssertions.

---

## File Structure

- Create `src/EntityFrameworkCore.Locking/DistributedLockMode.cs`: public enum for distributed lock modes.
- Modify `src/EntityFrameworkCore.Locking/Abstractions/IAdvisoryLockProvider.cs`: add `DistributedLockMode mode` to every acquisition method.
- Modify `src/EntityFrameworkCore.Locking/Extensions/DatabaseFacadeDistributedLockExtensions.cs`: add optional mode parameters, validate enum values, and pass mode to providers.
- Modify `tests/EntityFrameworkCore.Locking.Tests/DistributedLockUnitTests.cs`: verify default and explicit mode propagation through the public API.
- Modify `src/EntityFrameworkCore.Locking.PostgreSQL/PostgresAdvisoryLockProvider.cs`: choose exclusive or shared PostgreSQL advisory functions.
- Modify `src/EntityFrameworkCore.Locking.SqlServer/SqlServerAdvisoryLockProvider.cs`: pass `Exclusive` or `Shared` to `sp_getapplock`.
- Modify `src/EntityFrameworkCore.Locking.MySql/MySqlAdvisoryLockProvider.cs`: reject shared mode before issuing `GET_LOCK`.
- Modify `tests/EntityFrameworkCore.Locking.PostgreSQL.Tests/DistributedLockIntegrationTests.cs`: cover shared/shared compatibility and shared/exclusive conflicts.
- Modify `tests/EntityFrameworkCore.Locking.SqlServer.Tests/DistributedLockIntegrationTests.cs`: cover shared/shared compatibility and shared/exclusive conflicts.
- Modify `tests/EntityFrameworkCore.Locking.MySql.Tests/DistributedLockIntegrationTests.cs`: cover unsupported shared mode.
- Modify `README.md`: document shared distributed locks and provider support.

---

### Task 1: Core API Mode Plumbing

**Files:**
- Create: `src/EntityFrameworkCore.Locking/DistributedLockMode.cs`
- Modify: `src/EntityFrameworkCore.Locking/Abstractions/IAdvisoryLockProvider.cs`
- Modify: `src/EntityFrameworkCore.Locking/Extensions/DatabaseFacadeDistributedLockExtensions.cs`
- Modify: `tests/EntityFrameworkCore.Locking.Tests/DistributedLockUnitTests.cs`

- [ ] **Step 1: Write failing unit tests for default and explicit mode propagation**

Add these tests to `tests/EntityFrameworkCore.Locking.Tests/DistributedLockUnitTests.cs` inside `DistributedLockUnitTests`, near the existing `TryAcquireDistributedLockAsync_FreeKey_ReturnsHandle` test:

```csharp
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
```

Update the fake context and provider test helpers in the same file so the tests can inspect the provider:

```csharp
private static FakeDbContext CreateContext()
{
    var fakeConn = new FakeDbConnection();
    var fakeProvider = new FakeLockingProvider();

    var options = new DbContextOptionsBuilder<FakeDbContext>().UseSqlServer(fakeConn).Options;

    var extension = new LockingOptionsExtension(fakeProvider);
    options = (DbContextOptions<FakeDbContext>)options.WithExtension(extension);

    return new FakeDbContext(options, fakeProvider);
}

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

internal sealed class FakeLockingProvider : ILockingProvider
{
    private readonly FakeAdvisoryLockProvider _advisory = new();

    public FakeAdvisoryLockProvider Advisory => _advisory;
    public ILockSqlGenerator RowLockGenerator { get; } = new FakeLockSqlGenerator();
    public string ProviderName => "Fake";
    public IExceptionTranslator ExceptionTranslator { get; } = new FakeExceptionTranslator();
    public IAdvisoryLockProvider AdvisoryLockProvider => _advisory;
}
```

- [ ] **Step 2: Run the focused unit tests and verify they fail at compile time**

Run:

```bash
dotnet test tests/EntityFrameworkCore.Locking.Tests/EntityFrameworkCore.Locking.Tests.csproj --filter "FullyQualifiedName~DistributedLockUnitTests"
```

Expected: build failure because `DistributedLockMode` and mode-aware `IAdvisoryLockProvider` signatures do not exist.

- [ ] **Step 3: Add the public enum**

Create `src/EntityFrameworkCore.Locking/DistributedLockMode.cs`:

```csharp
namespace EntityFrameworkCore.Locking;

/// <summary>The type of distributed advisory lock to acquire.</summary>
public enum DistributedLockMode
{
    /// <summary>Exclusive lock. Only one holder can acquire the key.</summary>
    Exclusive = 0,

    /// <summary>Shared lock. Multiple shared holders can acquire the key while excluding exclusive holders.</summary>
    Shared = 1,
}
```

- [ ] **Step 4: Update the advisory provider interface**

Replace `src/EntityFrameworkCore.Locking/Abstractions/IAdvisoryLockProvider.cs` with:

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Implemented by each database provider to issue advisory (distributed) lock SQL.
/// Session-scoped: locks survive transactions and are released on dispose or connection close.
/// </summary>
public interface IAdvisoryLockProvider
{
    Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    );

    Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode
    );

    IDistributedLockHandle Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode
    );

    IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode
    );
}
```

- [ ] **Step 5: Update the public DatabaseFacade extensions**

In `src/EntityFrameworkCore.Locking/Extensions/DatabaseFacadeDistributedLockExtensions.cs`, update the method signatures and provider calls:

```csharp
public static async Task<IDistributedLockHandle> AcquireDistributedLockAsync(
    this DatabaseFacade database,
    string key,
    TimeSpan? timeout = null,
    CancellationToken ct = default,
    DistributedLockMode mode = DistributedLockMode.Exclusive
)
{
    var (ctx, provider, connection, openedByMe) = await PrepareAsync(database, key, mode, ct).ConfigureAwait(false);
    try
    {
        DistributedLockRegistry.RegisterOrThrow(ctx, connection, key);
        try
        {
            return await provider.AcquireAsync(ctx, connection, key, timeout, ct, mode).ConfigureAwait(false);
        }
        catch
        {
            DistributedLockRegistry.Unregister(ctx, connection, key);
            throw;
        }
    }
    catch
    {
        if (openedByMe)
            await connection.CloseAsync().ConfigureAwait(false);
        throw;
    }
}

public static async Task<IDistributedLockHandle?> TryAcquireDistributedLockAsync(
    this DatabaseFacade database,
    string key,
    CancellationToken ct = default,
    DistributedLockMode mode = DistributedLockMode.Exclusive
)
{
    var (ctx, provider, connection, openedByMe) = await PrepareAsync(database, key, mode, ct).ConfigureAwait(false);
    try
    {
        DistributedLockRegistry.RegisterOrThrow(ctx, connection, key);
        IDistributedLockHandle? handle;
        try
        {
            handle = await provider.TryAcquireAsync(ctx, connection, key, ct, mode).ConfigureAwait(false);
        }
        catch
        {
            DistributedLockRegistry.Unregister(ctx, connection, key);
            throw;
        }
        if (handle is null)
        {
            DistributedLockRegistry.Unregister(ctx, connection, key);
            if (openedByMe)
                await connection.CloseAsync().ConfigureAwait(false);
        }
        return handle;
    }
    catch
    {
        if (openedByMe)
            await connection.CloseAsync().ConfigureAwait(false);
        throw;
    }
}

public static IDistributedLockHandle AcquireDistributedLock(
    this DatabaseFacade database,
    string key,
    TimeSpan? timeout = null,
    DistributedLockMode mode = DistributedLockMode.Exclusive
)
{
    var (ctx, provider, connection, openedByMe) = PrepareSync(database, key, mode);
    try
    {
        DistributedLockRegistry.RegisterOrThrow(ctx, connection, key);
        try
        {
            return provider.Acquire(ctx, connection, key, timeout, mode);
        }
        catch
        {
            DistributedLockRegistry.Unregister(ctx, connection, key);
            throw;
        }
    }
    catch
    {
        if (openedByMe)
            connection.Close();
        throw;
    }
}

public static IDistributedLockHandle? TryAcquireDistributedLock(
    this DatabaseFacade database,
    string key,
    DistributedLockMode mode = DistributedLockMode.Exclusive
)
{
    var (ctx, provider, connection, openedByMe) = PrepareSync(database, key, mode);
    try
    {
        DistributedLockRegistry.RegisterOrThrow(ctx, connection, key);
        IDistributedLockHandle? handle;
        try
        {
            handle = provider.TryAcquire(ctx, connection, key, mode);
        }
        catch
        {
            DistributedLockRegistry.Unregister(ctx, connection, key);
            throw;
        }
        if (handle is null)
        {
            DistributedLockRegistry.Unregister(ctx, connection, key);
            if (openedByMe)
                connection.Close();
        }
        return handle;
    }
    catch
    {
        if (openedByMe)
            connection.Close();
        throw;
    }
}
```

Update the private prepare helpers and add mode validation:

```csharp
private static async Task<(
    DbContext ctx,
    IAdvisoryLockProvider provider,
    DbConnection connection,
    bool openedByMe
)> PrepareAsync(DatabaseFacade database, string key, DistributedLockMode mode, CancellationToken ct)
{
    ValidateKey(key);
    ValidateMode(mode);
    var ctx = GetContext(database);
    var provider = ResolveProvider(database);
    var connection = database.GetDbConnection();
    bool openedByMe = false;
    if (connection.State != ConnectionState.Open)
    {
        await connection.OpenAsync(ct).ConfigureAwait(false);
        openedByMe = true;
    }
    return (ctx, provider, connection, openedByMe);
}

private static (
    DbContext ctx,
    IAdvisoryLockProvider provider,
    DbConnection connection,
    bool openedByMe
) PrepareSync(DatabaseFacade database, string key, DistributedLockMode mode)
{
    ValidateKey(key);
    ValidateMode(mode);
    var ctx = GetContext(database);
    var provider = ResolveProvider(database);
    var connection = database.GetDbConnection();
    bool openedByMe = false;
    if (connection.State != ConnectionState.Open)
    {
        connection.Open();
        openedByMe = true;
    }
    return (ctx, provider, connection, openedByMe);
}

private static void ValidateMode(DistributedLockMode mode)
{
    if (!Enum.IsDefined(mode))
        throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'.");
}
```

- [ ] **Step 6: Update the fake advisory provider**

In `tests/EntityFrameworkCore.Locking.Tests/DistributedLockUnitTests.cs`, update `FakeAdvisoryLockProvider` signatures and record `LastMode`:

```csharp
internal sealed class FakeAdvisoryLockProvider : IAdvisoryLockProvider
{
    private readonly Dictionary<DbConnection, HashSet<string>> _held = new();
    private readonly object _gate = new();

    public DistributedLockMode? LastMode { get; private set; }

    public Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    )
    {
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
```

- [ ] **Step 7: Run the focused unit tests and verify they pass**

Run:

```bash
dotnet test tests/EntityFrameworkCore.Locking.Tests/EntityFrameworkCore.Locking.Tests.csproj --filter "FullyQualifiedName~DistributedLockUnitTests"
```

Expected: all `DistributedLockUnitTests` pass.

- [ ] **Step 8: Commit core API plumbing**

Run:

```bash
git add src/EntityFrameworkCore.Locking/DistributedLockMode.cs src/EntityFrameworkCore.Locking/Abstractions/IAdvisoryLockProvider.cs src/EntityFrameworkCore.Locking/Extensions/DatabaseFacadeDistributedLockExtensions.cs tests/EntityFrameworkCore.Locking.Tests/DistributedLockUnitTests.cs
git commit -m "feat: add distributed lock mode API"
```

Expected: commit succeeds.

---

### Task 2: PostgreSQL Shared Advisory Locks

**Files:**
- Modify: `src/EntityFrameworkCore.Locking.PostgreSQL/PostgresAdvisoryLockProvider.cs`

- [ ] **Step 1: Update PostgreSQL provider signatures and function selection**

Modify `PostgresAdvisoryLockProvider` so all four interface methods accept `DistributedLockMode mode`. Add these helpers inside the class:

```csharp
private static string GetAcquireFunction(DistributedLockMode mode) =>
    mode switch
    {
        DistributedLockMode.Exclusive => "pg_advisory_lock",
        DistributedLockMode.Shared => "pg_advisory_lock_shared",
        _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
    };

private static string GetTryAcquireFunction(DistributedLockMode mode) =>
    mode switch
    {
        DistributedLockMode.Exclusive => "pg_try_advisory_lock",
        DistributedLockMode.Shared => "pg_try_advisory_lock_shared",
        _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
    };

private static string GetReleaseFunction(DistributedLockMode mode) =>
    mode switch
    {
        DistributedLockMode.Exclusive => "pg_advisory_unlock",
        DistributedLockMode.Shared => "pg_advisory_unlock_shared",
        _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
    };
```

Update each place that assigns `CommandText`:

```csharp
lockCmd.CommandText = $"SELECT {GetAcquireFunction(mode)}($1)";
```

```csharp
cmd.CommandText = $"SELECT {GetTryAcquireFunction(mode)}($1)";
```

Update both handle creation call sites:

```csharp
return BuildHandle(context, connection, key, lockKey, mode);
```

Update `BuildHandle` signature and release SQL:

```csharp
private static IDistributedLockHandle BuildHandle(
    DbContext context,
    DbConnection connection,
    string key,
    long lockKey,
    DistributedLockMode mode
)
{
    async Task ReleaseAsync(CancellationToken ct)
    {
        DistributedLockRegistry.Unregister(context, connection, key);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {GetReleaseFunction(mode)}($1)";
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
        await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    void ReleaseSync()
    {
        DistributedLockRegistry.Unregister(context, connection, key);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {GetReleaseFunction(mode)}($1)";
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
        cmd.ExecuteScalar();
    }

    return new DistributedLockHandle(key, connection, openedByConnection: false, ReleaseAsync, ReleaseSync);
}
```

- [ ] **Step 2: Run PostgreSQL project build**

Run:

```bash
dotnet build src/EntityFrameworkCore.Locking.PostgreSQL/EntityFrameworkCore.Locking.PostgreSQL.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit PostgreSQL provider implementation**

Run:

```bash
git add src/EntityFrameworkCore.Locking.PostgreSQL/PostgresAdvisoryLockProvider.cs
git commit -m "feat(postgresql): support shared distributed locks"
```

Expected: commit succeeds.

---

### Task 3: SQL Server Shared Application Locks

**Files:**
- Modify: `src/EntityFrameworkCore.Locking.SqlServer/SqlServerAdvisoryLockProvider.cs`

- [ ] **Step 1: Update SQL Server provider signatures and lock mode mapping**

Modify `SqlServerAdvisoryLockProvider` so all four interface methods accept `DistributedLockMode mode`.

Update the acquire calls:

```csharp
await using var cmd = BuildAcquireCommand(connection, key, timeoutMs, mode);
```

```csharp
await using var cmd = BuildAcquireCommand(connection, key, timeoutMs: 0, mode);
```

```csharp
using var cmd = BuildAcquireCommand(connection, key, timeoutMs, mode);
```

```csharp
using var cmd = BuildAcquireCommand(connection, key, timeoutMs: 0, mode);
```

Replace `BuildAcquireCommand` with:

```csharp
private static DbCommand BuildAcquireCommand(
    DbConnection connection,
    string key,
    int timeoutMs,
    DistributedLockMode mode
)
{
    var cmd = connection.CreateCommand();
    cmd.CommandType = CommandType.StoredProcedure;
    cmd.CommandText = "sp_getapplock";

    AddParam(cmd, "@Resource", key);
    AddParam(cmd, "@LockMode", ToSqlServerLockMode(mode));
    AddParam(cmd, "@LockOwner", "Session");
    AddParam(cmd, "@LockTimeout", timeoutMs);
    AddParam(cmd, "@DbPrincipal", "public");

    var returnParam = cmd.CreateParameter();
    returnParam.ParameterName = "@ReturnValue";
    returnParam.Direction = ParameterDirection.ReturnValue;
    returnParam.DbType = DbType.Int32;
    cmd.Parameters.Add(returnParam);

    return cmd;
}

private static string ToSqlServerLockMode(DistributedLockMode mode) =>
    mode switch
    {
        DistributedLockMode.Exclusive => "Exclusive",
        DistributedLockMode.Shared => "Shared",
        _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
    };
```

- [ ] **Step 2: Run SQL Server project build**

Run:

```bash
dotnet build src/EntityFrameworkCore.Locking.SqlServer/EntityFrameworkCore.Locking.SqlServer.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit SQL Server provider implementation**

Run:

```bash
git add src/EntityFrameworkCore.Locking.SqlServer/SqlServerAdvisoryLockProvider.cs
git commit -m "feat(sqlserver): support shared distributed locks"
```

Expected: commit succeeds.

---

### Task 4: MySQL Unsupported Shared Mode

**Files:**
- Modify: `src/EntityFrameworkCore.Locking.MySql/MySqlAdvisoryLockProvider.cs`

- [ ] **Step 1: Update MySQL provider signatures and guard shared mode**

Modify `MySqlAdvisoryLockProvider` so all four interface methods accept `DistributedLockMode mode`.

Add this helper inside the class:

```csharp
private static void EnsureSupportedMode(DistributedLockMode mode)
{
    if (mode == DistributedLockMode.Exclusive)
        return;

    if (mode == DistributedLockMode.Shared)
        throw new LockingConfigurationException(
            "MySQL GET_LOCK does not support shared distributed locks."
        );

    throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'.");
}
```

Call it as the first statement in all four acquisition methods:

```csharp
EnsureSupportedMode(mode);
```

- [ ] **Step 2: Run MySQL project build**

Run:

```bash
dotnet build src/EntityFrameworkCore.Locking.MySql/EntityFrameworkCore.Locking.MySql.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit MySQL provider implementation**

Run:

```bash
git add src/EntityFrameworkCore.Locking.MySql/MySqlAdvisoryLockProvider.cs
git commit -m "feat(mysql): reject shared distributed locks"
```

Expected: commit succeeds.

---

### Task 5: Provider Integration Tests

**Files:**
- Modify: `tests/EntityFrameworkCore.Locking.PostgreSQL.Tests/DistributedLockIntegrationTests.cs`
- Modify: `tests/EntityFrameworkCore.Locking.SqlServer.Tests/DistributedLockIntegrationTests.cs`
- Modify: `tests/EntityFrameworkCore.Locking.MySql.Tests/DistributedLockIntegrationTests.cs`

- [ ] **Step 1: Add PostgreSQL shared mode tests**

Add these tests to `tests/EntityFrameworkCore.Locking.PostgreSQL.Tests/DistributedLockIntegrationTests.cs`:

```csharp
[Fact]
public async Task SharedLocks_TwoContexts_CanHoldSameKeyConcurrently()
{
    const string key = "pg-shared-concurrent";

    await using var ctxA = CreateContext();
    await using var handleA = await ctxA.Database.AcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );

    await using var ctxB = CreateContext();
    await using var handleB = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );

    handleB.Should().NotBeNull();
}

[Fact]
public async Task SharedLock_BlocksExclusiveTryAcquireUntilReleased()
{
    const string key = "pg-shared-blocks-exclusive";

    await using var ctxA = CreateContext();
    var sharedHandle = await ctxA.Database.AcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );

    await using var ctxB = CreateContext();
    var exclusiveHandle = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Exclusive
    );
    exclusiveHandle.Should().BeNull();

    await sharedHandle.DisposeAsync();

    exclusiveHandle = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Exclusive
    );
    exclusiveHandle.Should().NotBeNull();
    await exclusiveHandle!.DisposeAsync();
}

[Fact]
public async Task ExclusiveLock_BlocksSharedTryAcquireUntilReleased()
{
    const string key = "pg-exclusive-blocks-shared";

    await using var ctxA = CreateContext();
    var exclusiveHandle = await ctxA.Database.AcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Exclusive
    );

    await using var ctxB = CreateContext();
    var sharedHandle = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );
    sharedHandle.Should().BeNull();

    await exclusiveHandle.DisposeAsync();

    sharedHandle = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );
    sharedHandle.Should().NotBeNull();
    await sharedHandle!.DisposeAsync();
}
```

- [ ] **Step 2: Add SQL Server shared mode tests**

Add these tests to `tests/EntityFrameworkCore.Locking.SqlServer.Tests/DistributedLockIntegrationTests.cs`:

```csharp
[Fact]
public async Task SharedLocks_TwoContexts_CanHoldSameKeyConcurrently()
{
    const string key = "ss-shared-concurrent";

    await using var ctxA = CreateContext();
    await using var handleA = await ctxA.Database.AcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );

    await using var ctxB = CreateContext();
    await using var handleB = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );

    handleB.Should().NotBeNull();
}

[Fact]
public async Task SharedLock_BlocksExclusiveTryAcquireUntilReleased()
{
    const string key = "ss-shared-blocks-exclusive";

    await using var ctxA = CreateContext();
    var sharedHandle = await ctxA.Database.AcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );

    await using var ctxB = CreateContext();
    var exclusiveHandle = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Exclusive
    );
    exclusiveHandle.Should().BeNull();

    await sharedHandle.DisposeAsync();

    exclusiveHandle = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Exclusive
    );
    exclusiveHandle.Should().NotBeNull();
    await exclusiveHandle!.DisposeAsync();
}

[Fact]
public async Task ExclusiveLock_BlocksSharedTryAcquireUntilReleased()
{
    const string key = "ss-exclusive-blocks-shared";

    await using var ctxA = CreateContext();
    var exclusiveHandle = await ctxA.Database.AcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Exclusive
    );

    await using var ctxB = CreateContext();
    var sharedHandle = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );
    sharedHandle.Should().BeNull();

    await exclusiveHandle.DisposeAsync();

    sharedHandle = await ctxB.Database.TryAcquireDistributedLockAsync(
        key,
        mode: DistributedLockMode.Shared
    );
    sharedHandle.Should().NotBeNull();
    await sharedHandle!.DisposeAsync();
}
```

- [ ] **Step 3: Add MySQL unsupported shared mode test**

Add the missing using to `tests/EntityFrameworkCore.Locking.MySql.Tests/DistributedLockIntegrationTests.cs`:

```csharp
using EntityFrameworkCore.Locking.Exceptions;
```

Add this test to the same file:

```csharp
[Fact]
public async Task SharedMode_ThrowsLockingConfigurationException()
{
    await using var ctx = CreateContext();

    Func<Task> act = () => ctx.Database.AcquireDistributedLockAsync(
        "mysql-shared-unsupported",
        mode: DistributedLockMode.Shared
    );

    await act.Should()
        .ThrowAsync<LockingConfigurationException>()
        .WithMessage("*GET_LOCK*shared distributed locks*");
}
```

- [ ] **Step 4: Run provider distributed lock tests**

Run the tests that match available local services:

```bash
dotnet test tests/EntityFrameworkCore.Locking.PostgreSQL.Tests/EntityFrameworkCore.Locking.PostgreSQL.Tests.csproj --filter "FullyQualifiedName~DistributedLockIntegrationTests"
dotnet test tests/EntityFrameworkCore.Locking.SqlServer.Tests/EntityFrameworkCore.Locking.SqlServer.Tests.csproj --filter "FullyQualifiedName~DistributedLockIntegrationTests"
dotnet test tests/EntityFrameworkCore.Locking.MySql.Tests/EntityFrameworkCore.Locking.MySql.Tests.csproj --filter "FullyQualifiedName~DistributedLockIntegrationTests"
```

Expected: each project passes when its database container is available. If a provider database is unavailable, record the exact failure and run at least the corresponding project build.

- [ ] **Step 5: Commit integration tests**

Run:

```bash
git add tests/EntityFrameworkCore.Locking.PostgreSQL.Tests/DistributedLockIntegrationTests.cs tests/EntityFrameworkCore.Locking.SqlServer.Tests/DistributedLockIntegrationTests.cs tests/EntityFrameworkCore.Locking.MySql.Tests/DistributedLockIntegrationTests.cs
git commit -m "test: cover shared distributed lock behavior"
```

Expected: commit succeeds.

---

### Task 6: README and Full Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update distributed lock examples**

In `README.md`, in the distributed locks section after the timeout example, add:

```csharp
// Shared lock where supported
await using var handle = await ctx.Database.AcquireDistributedLockAsync(
    "catalog:read", mode: DistributedLockMode.Shared);
```

- [ ] **Step 2: Update provider behavior table**

In the advisory lock provider behavior table, add a row for shared distributed locks:

```markdown
| Shared distributed locks | `pg_advisory_lock_shared` | Not supported by `GET_LOCK` | `sp_getapplock @LockMode='Shared'` |
```

Add this note below the table:

```markdown
MySQL distributed locks use `GET_LOCK`, which is exclusive-only. Requesting `DistributedLockMode.Shared` on MySQL throws `LockingConfigurationException`.
```

- [ ] **Step 3: Run formatting and full build/test verification**

Run:

```bash
dotnet format efcore-locking.sln --verify-no-changes
dotnet test efcore-locking.sln
git diff --check
```

Expected: formatting verification passes, the solution test run passes when provider databases are available, and `git diff --check` prints no whitespace errors.

- [ ] **Step 4: Commit docs**

Run:

```bash
git add README.md
git commit -m "docs: document shared distributed locks"
```

Expected: commit succeeds.

---

## Self-Review

- Spec coverage: API mode parameter, PostgreSQL support, SQL Server support, MySQL rejection, registry semantics, error behavior, README updates, and tests are covered by Tasks 1 through 6.
- Red-flag scan: no incomplete markers are intentionally present.
- Type consistency: the plan consistently uses `DistributedLockMode`, `Exclusive`, `Shared`, and the final parameter ordering `timeout`, `ct`, `mode` for async acquire.
