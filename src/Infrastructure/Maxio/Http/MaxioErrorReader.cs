using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Normalises the several error payload shapes the specification declares into a flat list of messages.
/// </summary>
/// <remarks>
/// The specification models errors as <c>Error List Response</c> (<c>{"errors":["..."]}</c>),
/// <c>Single String Error Response</c> (<c>{"errors":"..."}</c>),
/// <c>Error String Map Response</c> (<c>{"errors":{"field":"..."}}</c>),
/// <c>Error Array Map Response</c> (<c>{"errors":{"field":["..."]}}</c>),
/// <c>Single Error Response</c> (<c>{"error":"..."}</c>), and — for a few operations — a bare JSON string.
/// </remarks>
internal static class MaxioErrorReader
{
    private const int MaxRawBodyLength = 512;

    public static IReadOnlyList<string> Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var messages = ReadRoot(document.RootElement).ToArray();
            return messages.Length > 0 ? messages : new[] { Truncate(body) };
        }
        catch (JsonException)
        {
            return new[] { Truncate(body) };
        }
    }

    private static IEnumerable<string> ReadRoot(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("errors", out var errors))
        {
            foreach (var message in Flatten(errors))
            {
                yield return message;
            }
        }

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
        {
            var value = error.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> Flatten(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var message in Flatten(item))
                    {
                        yield return message;
                    }
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in Flatten(property.Value))
                    {
                        yield return $"{property.Name}: {message}";
                    }
                }

                break;
        }
    }

    private static string Truncate(string body)
    {
        var collapsed = body.Trim();
        return collapsed.Length <= MaxRawBodyLength ? collapsed : collapsed[..MaxRawBodyLength] + "...";
    }
}
