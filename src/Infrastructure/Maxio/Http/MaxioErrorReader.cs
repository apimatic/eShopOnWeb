using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Extracts human readable messages from a Maxio error payload.
/// </summary>
/// <remarks>
/// The specification models errors in more than one shape. <c>Error List Response</c> carries a
/// plain array of strings, while <c>Customer Error Response</c> carries either that array or a
/// <c>Customer Error</c> object whose properties are messages. A few endpoints answer with a bare
/// string body. All of those are folded into a flat list here, and anything unrecognised degrades
/// to the raw body rather than being dropped.
/// </remarks>
internal static class MaxioErrorReader
{
    private const int MaxRawBodyLength = 2000;

    public static IReadOnlyList<string> Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var messages = new List<string>();
            Collect(document.RootElement, messages);
            if (messages.Count > 0)
            {
                return messages;
            }
        }
        catch (JsonException)
        {
            // Not JSON. Fall through to the raw body.
        }

        return new[] { Truncate(body.Trim()) };
    }

    private static void Collect(JsonElement element, List<string> messages)
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
                    // "errors" and "error" are the payload wrappers; anything else is a field name
                    // whose value is the message about that field.
                    if (property.NameEquals("errors") || property.NameEquals("error"))
                    {
                        Collect(property.Value, messages);
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        Add($"{property.Name}: {property.Value.GetString()}", messages);
                    }
                    else
                    {
                        Collect(property.Value, messages);
                    }
                }

                break;
        }
    }

    private static void Add(string? message, List<string> messages)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(Truncate(message.Trim()));
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaxRawBodyLength ? value : value[..MaxRawBodyLength] + "...";
}
