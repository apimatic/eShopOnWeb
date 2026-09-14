using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Maxio's 422 error payloads are either <c>{"errors": ["msg", ...]}</c> or
/// <c>{"errors": {"attribute": ["msg", ...]}}</c>. This flattens either shape into a single list.
/// </summary>
internal class MaxioErrorsJsonConverter : JsonConverter<JsonErrors>
{
    public override JsonErrors? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new JsonErrors();
        using var doc = JsonDocument.ParseValue(ref reader);
        Flatten(doc.RootElement, result.Messages);
        return result;
    }

    private static void Flatten(JsonElement element, System.Collections.Generic.List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                into.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, into);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, into);
                }
                break;
        }
    }

    public override void Write(Utf8JsonWriter writer, JsonErrors value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var message in value.Messages)
        {
            writer.WriteStringValue(message);
        }
        writer.WriteEndArray();
    }
}
