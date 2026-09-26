using System.Data;
using Dapper;

namespace Sms.Infrastructure.Persistence;

internal sealed class PostgresDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public static readonly PostgresDateTimeOffsetHandler Instance = new();

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
        parameter.Value = value.UtcDateTime;

    public override DateTimeOffset Parse(object value) =>
        value switch
        {
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
            _ => throw new DataException($"Cannot convert {value.GetType().Name} to DateTimeOffset.")
        };
}
