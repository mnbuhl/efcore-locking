using System.Collections;
using System.Data.Common;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests;

public class TranslatingDbDataReaderTests
{
    [Fact]
    public void GetFieldValue_DelegatesProviderSpecificTypedReadToInnerReader()
    {
        var expected = new DateTimeOffset(2026, 5, 14, 10, 15, 30, TimeSpan.Zero);
        var reader = new TranslatingDbDataReader(
            new ProviderSpecificDateTimeOffsetReader(expected),
            new NoopTranslator()
        );

        var value = reader.GetFieldValue<DateTimeOffset>(0);

        value.Should().Be(expected);
    }

    [Fact]
    public async Task GetFieldValueAsync_DelegatesProviderSpecificTypedReadToInnerReader()
    {
        var expected = new DateTimeOffset(2026, 5, 14, 10, 15, 30, TimeSpan.Zero);
        var reader = new TranslatingDbDataReader(
            new ProviderSpecificDateTimeOffsetReader(expected),
            new NoopTranslator()
        );

        var value = await reader.GetFieldValueAsync<DateTimeOffset>(0);

        value.Should().Be(expected);
    }

    private sealed class NoopTranslator : IExceptionTranslator
    {
        public LockingException? Translate(Exception exception) => null;
    }

    private sealed class ProviderSpecificDateTimeOffsetReader(DateTimeOffset value) : DbDataReader
    {
        public override int FieldCount => 1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => 1;

        public override bool Read() => true;

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public override bool NextResult() => false;

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

        public override byte GetByte(int ordinal) => throw new NotSupportedException();

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            throw new NotSupportedException();

        public override char GetChar(int ordinal) => throw new NotSupportedException();

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            throw new NotSupportedException();

        public override string GetDataTypeName(int ordinal) => "timestamp with time zone";

        public override DateTime GetDateTime(int ordinal) => value.UtcDateTime;

        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

        public override double GetDouble(int ordinal) => throw new NotSupportedException();

        public override Type GetFieldType(int ordinal) => typeof(DateTime);

        public override T GetFieldValue<T>(int ordinal)
        {
            if (typeof(T) == typeof(DateTimeOffset))
                return (T)(object)value;

            return base.GetFieldValue<T>(ordinal);
        }

        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) =>
            Task.FromResult(GetFieldValue<T>(ordinal));

        public override float GetFloat(int ordinal) => throw new NotSupportedException();

        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

        public override short GetInt16(int ordinal) => throw new NotSupportedException();

        public override int GetInt32(int ordinal) => throw new NotSupportedException();

        public override long GetInt64(int ordinal) => throw new NotSupportedException();

        public override string GetName(int ordinal) => ordinal == 0 ? "Date" : throw new IndexOutOfRangeException();

        public override int GetOrdinal(string name) => name == "Date" ? 0 : throw new IndexOutOfRangeException(name);

        public override string GetString(int ordinal) => throw new NotSupportedException();

        public override object GetValue(int ordinal) => value.UtcDateTime;

        public override int GetValues(object[] values)
        {
            values[0] = GetValue(0);
            return 1;
        }

        public override bool IsDBNull(int ordinal) => false;

        public override IEnumerator GetEnumerator()
        {
            yield break;
        }
    }
}
