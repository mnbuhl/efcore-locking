using System.Runtime.CompilerServices;

// Provider assemblies need access to LockContext, LockTagConstants, UnsafeShapeDetector,
// LockingValidationInterceptor, and LockingOptionsExtension.
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.PostgreSQL")]
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.MySql")]
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.SqlServer")]
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.Oracle")]

// Benchmark assemblies
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.Benchmarks")]

// Test assemblies
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.Tests")]
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.PostgreSQL.Tests")]
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.MySql.Tests")]
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.SqlServer.Tests")]
[assembly: InternalsVisibleTo("EntityFrameworkCore.Locking.Oracle.Tests")]
