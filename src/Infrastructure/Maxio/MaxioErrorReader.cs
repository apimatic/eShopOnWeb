using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Flattens a Maxio error payload into messages. Advanced Billing uses several error shapes across
/// the specification and this reader handles each of them:
/// <list type="bullet">
///   <item><c>Error-List-Response</c>: <c>{ "errors": ["...", "..."] }</c></item>
///   <item><c>Customer-Error-Response</c>: <c>{ "errors": { "customer": "..." } }</c></item>
///   <item><c>Error-Array-Map-Response</c>: <c>{ "errors": { "field": ["..."] } }</c></item>
///   <item><c>Single-Error-Response</c>: <c>{ "error": "..." }</c></item>
/// </list>
/// Anything it cannot parse falls back to a trimmed snippet of the raw body.
/// </summary>
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
            var messages = new List<string>();

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("errors", out var errors))
                {
                    Collect(errors, messages);
                }

                if (document.RootElement.TryGetProperty("error", out var singleError))
                {
                    Collect(singleError, messages);
                }
            }
            else
            {
                Collect(document.RootElement, messages);
            }

            return messages.Count > 0 ? messages : new[] { Truncate(body) };
        }
        catch (JsonException)
        {
            return new[] { Truncate(body) };
        }
    }

    private static void Collect(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value.Trim());
                }

                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                messages.Add(element.ToString());
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
                    var before = messages.Count;
                    Collect(property.Value, messages);
                    for (var i = before; i < messages.Count; i++)
                    {
                        messages[i] = $"{property.Name}: {messages[i]}";
                    }
                }

                break;
        }
    }

    private static string Truncate(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= MaxRawBodyLength ? trimmed : trimmed[..MaxRawBodyLength] + "...";
    }
}
