using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Flattens the error bodies Maxio returns into a list of messages.
/// <para>
/// The specification uses several error envelopes depending on the operation, and this parser
/// accepts all of them (maxio-spec/components/schemas/errors/):
/// </para>
/// <list type="bullet">
///   <item><c>Error-List-Response</c>: <c>{"errors": ["Reference: must be unique."]}</c></item>
///   <item><c>Customer-Error-Response</c>: <c>{"errors": {"customer": "can't be blank"}}</c></item>
///   <item><c>Error-String-Map-Response</c>: <c>{"errors": {"product_handle": "is invalid"}}</c></item>
///   <item><c>Error-Array-Map-Response</c>: <c>{"errors": {"base": ["is invalid"]}}</c></item>
///   <item><c>Single-Error-Response</c>: <c>{"error": "..."}</c></item>
/// </list>
/// </summary>
public static class MaxioErrorParser
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
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors))
            {
                Collect(errors, prefix: null, messages);
            }

            if (root.TryGetProperty("error", out var singleError) && singleError.ValueKind == JsonValueKind.String)
            {
                Add(messages, singleError.GetString());
            }

            return messages;
        }
        catch (JsonException)
        {
            // A non-JSON body (an HTML error page from an edge proxy, say) carries no structured
            // detail worth surfacing; the status code and raw body are reported separately.
            return Array.Empty<string>();
        }
    }

    private static void Collect(JsonElement element, string? prefix, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Add(messages, Compose(prefix, element.GetString()));
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
        }
    }

    private static string? Compose(string? prefix, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(prefix) ? message : $"{prefix}: {message}";
    }

    private static void Add(List<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message.Trim());
        }
    }
}
