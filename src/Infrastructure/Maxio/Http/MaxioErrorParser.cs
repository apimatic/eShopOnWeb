using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Extracts human-readable messages from a Maxio error response body. Per the OpenAPI error schemas,
/// the <c>errors</c> field may be an array of strings, an object of field→message, or a bare string.
/// </summary>
public static class MaxioErrorParser
{
    public static IReadOnlyList<string> Parse(string? body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                    {
                        AppendValue(messages, item);
                    }
                    break;

                case JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        var value = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.ToString();
                        messages.Add($"{property.Name}: {value}");
                    }
                    break;

                case JsonValueKind.String:
                    var text = errors.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        messages.Add(text!);
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            // Non-JSON body (e.g. a plain-text 404). Fall back to the raw body, trimmed.
            messages.Add(body.Trim());
        }

        return messages;
    }

    private static void AppendValue(List<string> messages, JsonElement item)
    {
        var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            messages.Add(value!);
        }
    }
}
