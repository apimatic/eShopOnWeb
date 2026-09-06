using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Extracts human readable messages from a Maxio error body.
/// </summary>
/// <remarks>
/// The spec uses several error shapes across operations, so this handles all of them and degrades
/// gracefully rather than throwing while building an error:
/// <list type="bullet">
///   <item><c>Error-List-Response</c>: <c>{ "errors": ["...", "..."] }</c></item>
///   <item><c>Customer-Error-Response</c>: <c>{ "errors": { "customer": "can't be blank" } }</c> or the array form</item>
///   <item><c>Single-Error-Response</c>: <c>{ "error": "..." }</c></item>
///   <item>a bare JSON string, e.g. the 404 on "List Products for Product Family"</item>
/// </list>
/// </remarks>
internal static class MaxioErrorReader
{
    public const int MaxRawBodyLength = 2048;

    public static IReadOnlyList<string> ReadErrors(string? body)
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
                case JsonValueKind.Object:
                    if (root.TryGetProperty("errors", out var errors))
                    {
                        Collect(messages, errors, prefix: null);
                    }

                    if (root.TryGetProperty("error", out var singleError))
                    {
                        Collect(messages, singleError, prefix: null);
                    }

                    break;
                case JsonValueKind.Array:
                    Collect(messages, root, prefix: null);
                    break;
            }

            return messages;
        }
        catch (JsonException)
        {
            return new[] { Truncate(body!) };
        }
    }

    public static string Truncate(string body) =>
        body.Length <= MaxRawBodyLength ? body : body[..MaxRawBodyLength] + "…";

    private static void Collect(List<string> messages, JsonElement element, string? prefix)
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
                    Collect(messages, item, prefix);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(messages, property.Value, property.Name);
                }

                break;
        }
    }

    private static string? Prefixed(string? prefix, string? value) =>
        string.IsNullOrWhiteSpace(prefix) ? value : $"{prefix}: {value}";

    private static void AddIfPresent(List<string> messages, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            messages.Add(value.Trim());
        }
    }
}
