using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Flattens the error payloads described by the specification into a list of messages.
/// The <c>errors</c> member is variously an array of strings (<c>Error-Array-Response</c>), an object
/// of string values (<c>Customer-Error</c>, <c>Error-String-Map</c>) or an object of string arrays
/// (<c>Error-Array-Map</c>), so all three shapes are handled.
/// </summary>
internal static class MaxioErrorParser
{
    public static IReadOnlyList<string> Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();
            Flatten(errors, prefix: null, messages);
            return messages;
        }
        catch (JsonException)
        {
            // A non-JSON body (an HTML error page from an edge proxy, for example) carries no
            // structured detail worth surfacing; the status code already tells the caller enough.
            return Array.Empty<string>();
        }
    }

    private static void Flatten(JsonElement element, string? prefix, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Add(element.GetString(), prefix, messages);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, prefix, messages);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, property.Name, messages);
                }

                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                Add(element.ToString(), prefix, messages);
                break;
        }
    }

    private static void Add(string? message, string? prefix, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        messages.Add(string.IsNullOrEmpty(prefix) ? message! : $"{prefix}: {message}");
    }
}
