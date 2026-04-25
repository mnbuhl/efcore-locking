using EntityFrameworkCore.Locking.Oracle.Tests.Fixtures;
using Xunit;

namespace EntityFrameworkCore.Locking.Oracle.Tests;

[CollectionDefinition("Oracle")]
public sealed class OracleCollection : ICollectionFixture<OracleFixture> { }
