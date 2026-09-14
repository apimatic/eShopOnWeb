using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Maxio error responses aren't shaped consistently across endpoints (see the various
/// schemas under maxio-spec/components/schemas/errors/): sometimes `errors` is an array
/// of strings, sometimes an object mapping field names to a string or array of strings.
/// This walks whatever shape shows up and flattens it into readable messages.
/// </summary>
internal static class MaxioErrorParser
{
    public static IReadOnlyList<string> Parse(string body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                Flatten(errors, null, messages);
            }
            else
            {
                Flatten(root, null, messages);
            }
        }
        catch (JsonException)
        {
            messages.Add(body);
        }

        return messages;
    }

    private static void Flatten(JsonElement element, string? fieldName, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    into.Add(fieldName == null ? text! : $"{fieldName}: {text}");
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, fieldName, into);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, property.Name, into);
                }
                break;
        }
    }
}
