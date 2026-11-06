using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace TrainingManagement.Api.Infrastructure;
// SQL Server datetime2 has no DateTimeKind. All application timestamps use UTC.
public sealed class UtcDateTimeConverter:JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        if(reader.TokenType!=JsonTokenType.String||!DateTimeOffset.TryParse(reader.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var value))throw new JsonException("日期格式不正確。");
        return value.UtcDateTime;
    }
    public override void Write(Utf8JsonWriter writer,DateTime value,JsonSerializerOptions options)=>writer.WriteStringValue((value.Kind==DateTimeKind.Local?value.ToUniversalTime():DateTime.SpecifyKind(value,DateTimeKind.Utc)).ToString("O",CultureInfo.InvariantCulture));
}
