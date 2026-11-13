using AwesomeAssertions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests;

public class LockTagScanTests
{
    private const string Tag = "__efcore_locking:ForUpdate:Wait:500";

    [Theory]
    [InlineData("-- ")]
    [InlineData(" \t\r\n-- ")]
    [InlineData("-- custom tag\n-- ")]
    [InlineData("-- first line\r\n-- second line\r\n\r\n-- ")]
    [InlineData("-- custom text containing ")]
    [InlineData("SET LOCAL lock_timeout = '500ms';\n-- ")]
    [InlineData("SET SESSION innodb_lock_wait_timeout = 1;\n-- ")]
    [InlineData("SET LOCK_TIMEOUT 500;\n-- ")]
    [InlineData("SET LOCK_TIMEOUT 0;\n-- ")]
    [InlineData("set lock_timeout 500;\n-- ")]
    [InlineData("/* custom prefix */\n-- ")]
    public void FindTag_FindsLeadingTagAndPreservesPreambles(string prefix)
    {
        var sql = prefix + Tag + "\r\nSELECT 1";
        LockTagConstants.FindTag(sql).Should().Be(prefix.Length);
    }

    [Fact]
    public void FindTag_FindsTagWithoutTrailingNewline()
    {
        LockTagConstants.FindTag("-- " + Tag).Should().Be(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("-")]
    [InlineData("--")]
    [InlineData("-- custom tag")]
    [InlineData("-- custom tag\r\nSELECT 1")]
    [InlineData("SELECT '__efcore_locking:ForUpdate:Wait:500'")]
    [InlineData("SELECT 1\n-- __efcore_locking:ForUpdate:Wait:500")]
    public void FindTag_DoesNotTreatSqlBodyAsQueryTags(string sql)
    {
        LockTagConstants.FindTag(sql).Should().Be(-1);
    }

    [Theory]
    [InlineData("Plain")]
    [InlineData("MultipleTags")]
    [InlineData("MultilineTag")]
    [InlineData("ComposedRawSql")]
    public void FindTag_FindsActualEfGeneratedQueryTags(string scenario)
    {
        using var context = new QueryContext();
        IQueryable<QueryRow> query = context.Set<QueryRow>();
        query = scenario switch
        {
            "MultipleTags" => query.TagWith("custom-a").TagWith("custom-b"),
            "MultilineTag" => query.TagWith("first line\nsecond line"),
            "ComposedRawSql" => context.Set<QueryRow>().FromSqlRaw("SELECT [Id] FROM [Items]"),
            _ => query,
        };
        var sql = query.Where(x => x.Id > 0).TagWith(Tag).ToQueryString();
        var index = LockTagConstants.FindTag(sql);
        index.Should().BeGreaterThanOrEqualTo(0);
        sql[index..].Should().StartWith(Tag);
    }

    private sealed class QueryContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlServer("Server=localhost;Database=unused");

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<QueryRow>().ToTable("Items").HasKey(x => x.Id);
    }

    private sealed class QueryRow
    {
        public int Id { get; set; }
    }

    [Fact]
    public void FindTag_LongUntaggedSql_ReturnsNotFound()
    {
        LockTagConstants.FindTag("-- custom\nSELECT " + new string('x', 65536)).Should().Be(-1);
    }
}
