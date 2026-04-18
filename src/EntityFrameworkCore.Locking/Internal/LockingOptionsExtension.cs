using EntityFrameworkCore.Locking.Abstractions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Locking.Internal;

/// <summary>
/// EF Core options extension that registers the ILockingProvider and ILockSqlGenerator
/// into the scoped service collection for a DbContext.
/// </summary>
public sealed class LockingOptionsExtension : IDbContextOptionsExtension
{
    private readonly ILockingProvider _provider;

    public LockingOptionsExtension(ILockingProvider provider)
    {
        _provider = provider;
    }

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.AddSingleton(_provider);
        services.AddSingleton<ILockingProvider>(_provider);
        services.AddSingleton(_provider.RowLockGenerator);
        services.AddSingleton<ILockSqlGenerator>(_provider.RowLockGenerator);
        services.AddSingleton(_provider.ExceptionTranslator);
        services.AddSingleton<IExceptionTranslator>(_provider.ExceptionTranslator);
    }

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension) { }

        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "EntityFrameworkCore.Locking";
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        public override int GetServiceProviderHashCode() => 0;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["EntityFrameworkCore.Locking:Enabled"] = "true";
    }
}
