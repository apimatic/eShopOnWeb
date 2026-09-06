using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Extracts human-readable messages from a Maxio error body.
/// </summary>
/// <remarks>
/// The spec models errors in several shapes and which one an operation uses varies:
/// <c>errors</c> as an array of strings (Error-List-Response), <c>errors</c> as an object keyed by
/// field (Customer-Error, Error-String-Map-Response), <c>errors</c> as a bare string
/// (Single-String-Error-Response), and <c>error</c> as a string (Single-Error-Response). Some
/// responses — 404s in particular — carry a bare string or nothing at all. This parser accepts all
/// of them rather than assuming one.
/// </remarks>
internal static class MaxioErrorParser
{
    private const int MaxRawLength = 500;

    public static IReadOnlyList<string> Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        var messages = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                AddIfPresent(messages, root.GetString());
                return messages;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                CollectArray(messages, root);
                return messages;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Truncated(body);
            }

            if (root.TryGetProperty("errors", out var errors))
            {
                Collect(messages, errors);
            }

            if (root.TryGetProperty("error", out var error))
            {
                Collect(messages, error);
            }

            return messages.Count > 0 ? messages : Truncated(body);
        }
        catch (JsonException)
        {
            // Not JSON at all (for example an HTML error page from an intermediary).
            return Truncated(body);
        }
    }

    private static void Collect(List<string> messages, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddIfPresent(messages, element.GetString());
                break;

            case JsonValueKind.Array:
                CollectArray(messages, element);
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var before = messages.Count;
                    Collect(messages, property.Value);
                    for (var i = before; i < messages.Count; i++)
                    {
                        messages[i] = $"{property.Name}: {messages[i]}";
                    }
                }

                break;
        }
    }

    private static void CollectArray(List<string> messages, JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            Collect(messages, item);
        }
    }

    private static void AddIfPresent(List<string> messages, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            messages.Add(value.Trim());
        }
    }

    private static IReadOnlyList<string> Truncated(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length > MaxRawLength)
        {
            trimmed = trimmed[..MaxRawLength] + "…";
        }

        return new[] { trimmed };
    }
}
