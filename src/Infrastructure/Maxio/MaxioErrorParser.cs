using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio error bodies are inconsistently shaped: sometimes <c>{"errors": ["msg", ...]}</c>,
/// sometimes <c>{"errors": {"customer": "msg"}}</c> or <c>{"errors": {"field": ["msg"]}}</c>.
/// This walks whichever shape shows up and flattens it to a single readable message.
/// </summary>
internal static class MaxioErrorParser
{
    public static string? ExtractMessage(string jsonBody)
    {
        if (string.IsNullOrWhiteSpace(jsonBody)) return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            if (!doc.RootElement.TryGetProperty("errors", out var errors)) return null;

            var messages = new List<string>();
            CollectMessages(errors, messages);
            return messages.Count == 0 ? null : string.Join(" ", messages);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CollectMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrEmpty(value)) messages.Add(value);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectMessages(item, messages);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) CollectMessages(property.Value, messages);
                break;
        }
    }
}
