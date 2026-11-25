using Newtonsoft.Json;
using System;
using System.Globalization;

public sealed class UInt128JsonConverter : JsonConverter<UInt128>
{
    public override void WriteJson(JsonWriter writer, UInt128 value, JsonSerializer serializer)
    {
        writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
    }

    public override UInt128 ReadJson(JsonReader reader, Type objectType, UInt128 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.Integer
            || !UInt128.TryParse(Convert.ToString(reader.Value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new JsonSerializationException("Expected an unsigned 128-bit integer.");

        return value;
    }
}
