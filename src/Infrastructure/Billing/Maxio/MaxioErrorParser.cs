using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Turns a Maxio error payload into a flat list of messages.
/// <para>
/// The specification uses several error models depending on the operation, all keyed on
/// <c>errors</c>:
/// <list type="bullet">
///   <item><description><c>Error-List-Response</c> &#8212; <c>{ "errors": ["..."] }</c></description></item>
///   <item><description><c>Customer-Error-Response</c> &#8212; <c>{ "errors": { "customer": "..." } }</c> or the array form</description></item>
///   <item><description><c>Error-Array-Map-Response</c> &#8212; <c>{ "errors": { "field": ["..."] } }</c></description></item>
///   <item><description><c>Single-Error-Response</c> &#8212; <c>{ "error": "..." }</c></description></item>
/// </list>
/// Some endpoints (for example <c>listProductsForProductFamily</c>) document a bare JSON string
/// body instead. Every one of those shapes is accepted here; anything unrecognised degrades to the
/// raw body text so no diagnostic detail is silently dropped.
/// </para>
/// </summary>
internal static class MaxioErrorParser
{
    private const int MaxRawBodyLength = 1024;

    public static IReadOnlyList<string> Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var messages = new List<string>();
            var root = document.RootElement;

            switch (root.ValueKind)
            {
                case JsonValueKind.String:
                    AddIfPresent(messages, root.GetString());
                    break;

                case JsonValueKind.Array:
                    CollectFrom(root, messages, prefix: null);
                    break;

                case JsonValueKind.Object:
                    if (root.TryGetProperty("errors", out var errors))
                    {
                        CollectFrom(errors, messages, prefix: null);
                    }

                    if (root.TryGetProperty("error", out var singleError))
                    {
                        CollectFrom(singleError, messages, prefix: null);
                    }

                    break;
            }

            return messages.Count > 0 ? messages : new[] { Truncate(body) };
        }
        catch (JsonException)
        {
            return new[] { Truncate(body) };
        }
    }

    private static void CollectFrom(JsonElement element, List<string> messages, string? prefix)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddIfPresent(messages, Prefixed(prefix, element.GetString()));
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                AddIfPresent(messages, Prefixed(prefix, element.ToString()));
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectFrom(item, messages, prefix);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectFrom(property.Value, messages, property.Name);
                }

                break;
        }
    }

    private static string? Prefixed(string? prefix, string? message) =>
        string.IsNullOrWhiteSpace(prefix) ? message : $"{prefix}: {message}";

    private static void AddIfPresent(List<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message.Trim());
        }
    }

    private static string Truncate(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= MaxRawBodyLength
            ? trimmed
            : trimmed[..MaxRawBodyLength] + "...";
    }
}
