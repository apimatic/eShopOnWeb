using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioErrorParser
{
    public static string Format(string? body, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Maxio Advanced Billing request failed with HTTP {statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var flattened = Flatten(errors);
                if (flattened.Count > 0)
                {
                    return $"Maxio Advanced Billing rejected the request: {string.Join("; ", flattened)}";
                }
            }
        }
        catch (JsonException)
        {
            // Fall through and return a truncated raw body.
        }

        var trimmed = body.Trim();
        if (trimmed.Length > 500)
        {
            trimmed = trimmed[..500] + "…";
        }

        return $"Maxio Advanced Billing request failed with HTTP {statusCode}: {trimmed}";
    }

    private static List<string> Flatten(JsonElement errors)
    {
        var messages = new List<string>();
        switch (errors.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(item.GetString()!);
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                messages.Add($"{property.Name}: {item.GetString()}");
                            }
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        messages.Add($"{property.Name}: {property.Value.GetString()}");
                    }
                }
                break;
            case JsonValueKind.String:
                messages.Add(errors.GetString()!);
                break;
        }

        return messages;
    }
}
