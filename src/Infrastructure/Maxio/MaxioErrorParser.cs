using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reads the two error envelopes the spec defines: <c>Error-List-Response</c>, whose <c>errors</c>
/// is an array of strings, and <c>Customer-Error-Response</c>, whose <c>errors</c> may instead be
/// an object keyed by field name. Anything else degrades to an empty list rather than throwing -
/// an error response must never turn into a parsing failure.
/// </summary>
internal static class MaxioErrorParser
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
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                    {
                        var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            messages.Add(text!);
                        }
                    }

                    break;

                case JsonValueKind.String:
                    var single = errors.GetString();
                    if (!string.IsNullOrWhiteSpace(single))
                    {
                        messages.Add(single!);
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
            // Not a JSON error envelope - the caller keeps the raw body for diagnostics.
        }

        return messages;
    }
}
