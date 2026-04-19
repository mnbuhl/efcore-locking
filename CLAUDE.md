# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Format check (required before committing)
dotnet csharpier check .

# Fix formatting
dotnet csharpier .

# Build all projects across all target frameworks
dotnet build --configuration Release

# Run unit tests (no Docker required)
dotnet test tests/EntityFrameworkCore.Locking.Tests

# Run integration tests for a single provider (requires Docker)
dotnet test tests/EntityFrameworkCore.Locking.PostgreSQL.Tests
dotnet test tests/EntityFrameworkCore.Locking.MySql.Tests
dotnet test tests/EntityFrameworkCore.Locking.SqlServer.Tests

# Run a single test by name
dotnet test tests/EntityFrameworkCore.Locking.PostgreSQL.Tests --filter "FullyQualifiedName~TestClassName.MethodName"

# Run all tests
dotnet test
```

## Architecture

The library is split into a provider-agnostic core and three database provider projects. Consumers reference only the provider project they need.

### Row-level locking flow

1. `ForUpdate()` / `ForShare()` (in `src/EntityFrameworkCore.Locking/Extensions/QueryableLockingExtensions.cs`) sets `LockContext.Current` (an `AsyncLocal<LockOptions?>`) and calls `TagWith("__efcore_locking:<mode>:<behavior>:<timeout>")` on the queryable.
2. Each provider's `LockingQuerySqlGenerator` (subclass of EF Core's `QuerySqlGenerator`) reads `LockContext.Current` and appends the provider-specific lock clause to the emitted SQL.
3. `LockingValidationInterceptor` (a `DbCommandInterceptor`) runs on every command:
   - In `ReaderExecuting`: validates an active transaction exists, then prepends any pre-statement SQL (e.g. `SET LOCAL lock_timeout`) returned by `ILockSqlGenerator.GeneratePreStatementSql`.
   - In `ReaderExecuted`: clears `LockContext.Current` and wraps the reader in `TranslatingDbDataReader` if the SQL contained the lock tag. Uses the **SQL tag** (not `LockContext`) as the signal because `AsyncLocal` changes in child continuations do not propagate back.
   - In `CommandFailed`: translates the exception via `IExceptionTranslator` and rethrows.
4. `UnsafeShapeDetector.ThrowIfUnsafe` is called by the SQL generator to reject incompatible query shapes (set operations, split queries).

### Distributed advisory locks flow

1. `DbContextDistributedLockExtensions.AcquireDistributedLockAsync()` resolves `IAdvisoryLockProvider` from `ILockingProvider.AdvisoryLockProvider`, opens the connection if needed, calls `DistributedLockRegistry.RegisterOrThrow` to prevent double-acquisition on the same context+connection, then delegates to the provider.
2. Each provider sends native advisory lock SQL (`pg_advisory_lock`, MySQL `GET_LOCK`, SQL Server `sp_getapplock`).
3. The returned `IDistributedLockHandle` releases the lock on dispose via a captured callback; `DistributedLockCleanupInterceptor` performs best-effort cleanup if handles are not disposed before connection close.

### Provider structure

Each of the three provider projects (`EntityFrameworkCore.Locking.PostgreSQL`, `.MySql`, `.SqlServer`) contains the same seven-file shape:

| File | Role |
|------|------|
| `*LockingProvider.cs` | `ILockingProvider` — wires together the other six components |
| `*AdvisoryLockProvider.cs` | `IAdvisoryLockProvider` — advisory lock SQL |
| `*LockSqlGenerator.cs` | `ILockSqlGenerator` — lock clause and pre-statement SQL strings |
| `*LockingQuerySqlGenerator.cs` | EF Core `QuerySqlGenerator` subclass — appends lock clause |
| `*LockingQuerySqlGeneratorFactory.cs` | Replaces `IQuerySqlGeneratorFactory` in EF Core DI |
| `*ExceptionTranslator.cs` | `IExceptionTranslator` — maps driver exceptions to typed `LockingException` subclasses |
| `*LockingServiceCollectionExtensions.cs` | `UseLocking()` — single DI registration entry point |

`UseLocking()` registers the provider via `LockingOptionsExtension`, replaces `IQuerySqlGeneratorFactory`, and adds `LockingValidationInterceptor` + `DistributedLockCleanupInterceptor`.

### Key constraints

- All row-level locking queries require an active transaction — the interceptor throws `InvalidOperationException` at execution time if none exists.
- Advisory locks are session-scoped and require no transaction.
- `ForUpdate` / `ForShare` are incompatible with set operations (`Union`/`Except`/`Intersect`) and `AsSplitQuery()`.
- PostgreSQL key hashing: advisory lock string keys are hashed via `XxHash32` combined with namespace prefix `0x45464C4B_00000000L` to produce a `bigint`.
- SQL Server uses table hints (`WITH (UPDLOCK, ROWLOCK)`) injected into the `FROM` clause, not trailing clauses like PostgreSQL/MySQL.

### Build configuration

- `Directory.Build.props` — shared across all projects: targets `net8.0;net9.0;net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, suppresses `EF1001` (intentional EF Core internal API use) and `CS1591`.
- `Directory.Packages.props` — centralized package version management; never add `Version="..."` to individual `.csproj` files.
- Assertion library in tests is `AwesomeAssertions` (not FluentAssertions).

### Test structure

Integration tests are split between shared base classes and provider-specific files.

**`tests/EntityFrameworkCore.Locking.Tests.Infrastructure/`** — shared base classes (class library, no `TestSdk`):

| File | Base class | Shared tests |
|------|------------|--------------|
| `IntegrationTestsBase.cs` (partial) | `IntegrationTestsBase` | 6 core integration tests |
| `IntegrationTests.QueryShapeTestsBase.cs` (partial) | `IntegrationTestsBase` | 8 query-shape tests |
| `ConcurrencyTestsBase.cs` | `ConcurrencyTestsBase` | 7 concurrency tests |
| `DistributedLockIntegrationTestsBase.cs` | `DistributedLockIntegrationTestsBase` | 8 distributed lock tests |

Each provider test project (`PostgreSQL.Tests`, `MySql.Tests`, `SqlServer.Tests`) inherits from these bases. xUnit discovers and runs inherited `[Fact]` methods in the concrete derived classes, so all shared tests execute locally inside each provider project (Docker required).

**Provider-specific tests** stay local to each project:
- **PostgreSQL**: `ForNoKeyUpdate`/`ForKeyShare` lock modes, `Except`/`Intersect` shape validation, `ForShare` skip-locked, advisory lock blocking/registry/cancellation tests.
- **MySQL**: `WaitTimeout` overridden to `TimeSpan.FromSeconds(1)` (innodb second-granularity); `DistributedLockAcquireTimeout` overridden to `TimeSpan.FromSeconds(1)` (GET_LOCK); long-key hashing test.
- **SqlServer**: `WaitTimeout` overridden to `TimeSpan.FromMilliseconds(500)`; `ForShare` throws `LockingConfigurationException`; cancellation test.

When adding a new integration test: if the behavior is identical across all three providers, add it to the appropriate base class. If it tests provider-specific SQL, exception mapping, or lock semantics, add it directly to that provider's test file.
