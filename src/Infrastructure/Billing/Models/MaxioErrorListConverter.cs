using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Models;

/// <summary>
/// Maxio returns errors as a string array or as an object of field -> message pairs.
/// </summary>
internal sealed class MaxioErrorListConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Array.Empty<string>();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = JsonSerializer.Deserialize<List<string>>(ref reader, options);
            return list ?? new List<string>();
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var messages = new List<string>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                messages.Add($"{property.Name}: {property.Value.ToString()}");
            }
            return messages;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return new[] { reader.GetString() ?? string.Empty };
        }

        reader.Skip();
        return Array.Empty<string>();
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
