using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Extracts the human-readable messages out of a Maxio error body.
/// </summary>
/// <remarks>
/// Maxio's <c>errors</c> member is not a single shape: validation failures return an array of
/// strings (<c>{"errors":["Reference: must be unique - that value has been taken."]}</c>), some
/// endpoints return a per-field object (<c>{"errors":{"email":["is invalid"]}}</c>), and a few
/// return a bare string. Anything unparseable falls back to the raw body so no detail is lost.
/// </remarks>
public static class MaxioErrorReader
{
    private const int MaxRawBodyLength = 2000;

    public static IReadOnlyList<string> ReadErrors(string? body)
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
                return new[] { Truncate(body) };
            }

            var messages = new List<string>();
            Collect(errors, prefix: null, messages);
            return messages.Count > 0 ? messages : new[] { Truncate(body) };
        }
        catch (JsonException)
        {
            return new[] { Truncate(body) };
        }
    }

    public static string Truncate(string body) =>
        body.Length <= MaxRawBodyLength ? body : body[..MaxRawBodyLength] + "...";

    private static void Collect(JsonElement element, string? prefix, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Add(element.GetString(), prefix, messages);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, prefix, messages);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, property.Name, messages);
                }

                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                Add(element.ToString(), prefix, messages);
                break;
        }
    }

    private static void Add(string? message, string? prefix, ICollection<string> messages)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        messages.Add(string.IsNullOrEmpty(prefix) ? message! : $"{prefix}: {message}");
    }
}
