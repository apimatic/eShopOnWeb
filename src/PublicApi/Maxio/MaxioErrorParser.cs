using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Best-effort extraction of human readable messages from the error payloads Maxio returns.
/// </summary>
internal static class MaxioErrorParser
{
    public static string? TryExtractMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return content.Length <= 2000 ? content : content.Substring(0, 2000);
            }

            var messages = new List<string>();
            CollectMessages(errors, messages);
            return messages.Count > 0 ? string.Join(" ", messages) : null;
        }
        catch (JsonException)
        {
            return content.Length <= 2000 ? content : content.Substring(0, 2000);
        }
    }

    private static void CollectMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMessages(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectMessages(property.Value, messages);
                }
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value);
                }
                break;
        }
    }
}
