using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Turns a Maxio error body into a flat list of human readable messages. The specification uses several
/// error envelopes depending on the operation, so every documented shape is handled here:
/// <list type="bullet">
///   <item><c>{ "errors": ["..."] }</c> — Error List Response / Error Array Response</item>
///   <item><c>{ "errors": { "field": "..." } }</c> — Error String Map Response, Customer Error</item>
///   <item><c>{ "errors": { "field": ["...", "..."] } }</c> — Error Array Map Response</item>
///   <item><c>{ "error": "..." }</c> — Single Error Response</item>
/// </list>
/// </summary>
internal static class MaxioErrorParser
{
    private const int MaxMessages = 25;

    public static IReadOnlyList<string> Parse(string? body)
    {
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body!);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                Collect(root, messages);
                return Finish(messages, body!);
            }

            if (root.TryGetProperty("errors", out var errors))
            {
                Collect(errors, messages);
            }

            if (root.TryGetProperty("error", out var singleError))
            {
                Collect(singleError, messages);
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. an HTML error page from an edge proxy) — fall through to the raw snippet.
        }

        return Finish(messages, body!);
    }

    private static IReadOnlyList<string> Finish(List<string> messages, string body)
    {
        if (messages.Count == 0)
        {
            var snippet = body.Trim();
            if (snippet.Length > 0)
            {
                messages.Add(snippet.Length > 500 ? snippet.Substring(0, 500) + "..." : snippet);
            }
        }

        return messages;
    }

    private static void Collect(JsonElement element, List<string> messages)
    {
        if (messages.Count >= MaxMessages)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Add(messages, element.GetString());
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                Add(messages, element.ToString());
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

                    // Prefix the field name onto the messages the property contributed, so that
                    // { "customer": "can't be blank" } reads as "customer: can't be blank".
                    for (var i = before; i < messages.Count; i++)
                    {
                        messages[i] = $"{property.Name}: {messages[i]}";
                    }
                }

                break;
        }
    }

    private static void Add(List<string> messages, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && messages.Count < MaxMessages)
        {
            messages.Add(value!.Trim());
        }
    }
}
