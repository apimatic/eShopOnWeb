using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Extracts human-readable messages from a Maxio error body.
/// </summary>
/// <remarks>
/// The specification models errors in more than one shape - <c>Error-List-Response</c> carries
/// <c>{"errors": ["..."]}</c> while <c>Customer-Error-Response</c> may instead carry
/// <c>{"errors": {"customer": "..."}}</c> - so the body is read structurally rather than
/// deserialized into a single fixed type.
/// </remarks>
internal static class MaxioErrorReader
{
    private const int MaxErrors = 20;

    public static IReadOnlyList<string> ReadErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var errors = new List<string>();

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                Collect(errorsElement, errors);
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object &&
                     document.RootElement.TryGetProperty("error", out var errorElement))
            {
                Collect(errorElement, errors);
            }

            return errors;
        }
        catch (JsonException)
        {
            // Non-JSON bodies (e.g. the plain-text 401 from the edge) carry nothing structured.
            return Array.Empty<string>();
        }
    }

    private static void Collect(JsonElement element, List<string> errors)
    {
        if (errors.Count >= MaxErrors)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(value);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, errors);
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in ReadPropertyMessages(property))
                    {
                        if (errors.Count >= MaxErrors)
                        {
                            return;
                        }

                        errors.Add(message);
                    }
                }
                break;
        }
    }

    private static IEnumerable<string> ReadPropertyMessages(JsonProperty property)
    {
        var nested = new List<string>();
        Collect(property.Value, nested);

        foreach (var message in nested)
        {
            yield return $"{property.Name}: {message}";
        }
    }
}
