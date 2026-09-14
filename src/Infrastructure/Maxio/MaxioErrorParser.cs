using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio's error payloads take several shapes depending on the endpoint (see
/// maxio-spec/components/schemas/errors): a plain string, an array of strings, or an object
/// mapping field name to message. This flattens all three into a list of human-readable messages.
/// </summary>
internal static class MaxioErrorParser
{
    public static IReadOnlyList<string> ParseErrors(string? responseBody)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.String:
                    messages.Add(errors.GetString() ?? string.Empty);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                    {
                        messages.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString());
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
            }
        }
        catch (JsonException)
        {
            messages.Add(responseBody);
        }

        return messages;
    }
}
