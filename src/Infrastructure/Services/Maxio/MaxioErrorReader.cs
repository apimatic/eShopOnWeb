using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Reads the messages out of a Maxio error payload. Maxio returns errors either as an array of
/// strings (Error-List-Response) or as a field-keyed object (Customer-Error-Response), and some
/// failures arrive with no JSON at all.
/// </summary>
internal static class MaxioErrorReader
{
    private const int MaxMessageLength = 300;
    private static readonly string[] None = Array.Empty<string>();

    public static IReadOnlyCollection<string> Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return None;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return None;
            }

            var messages = new List<string>();
            Collect(errors, messages);

            return messages;
        }
        catch (JsonException)
        {
            // A non-JSON body (an HTML error page, a proxy message) is never echoed back:
            // only its shape is known, not that it is safe to surface.
            return None;
        }
    }

    private static void Collect(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Add(element.GetString(), messages);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, messages);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, messages);
                }

                break;
        }
    }

    private static void Add(string? message, ICollection<string> messages)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var trimmed = message.Trim();
        messages.Add(trimmed.Length <= MaxMessageLength ? trimmed : trimmed[..MaxMessageLength]);
    }
}
