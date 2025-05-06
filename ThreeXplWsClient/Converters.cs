using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreeXplWsClient;

public class ThreeXplDateTimeConverter : JsonConverter<DateTime>
{
    // 2025-05-05 23:53:08
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var input = reader.GetString();
        if (input == null)
        {
            throw new NullReferenceException("Tried to convert a string to DateTime that was null");
        }
        return DateTime.ParseExact(input, Format, null);
    }
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format));
    }
}