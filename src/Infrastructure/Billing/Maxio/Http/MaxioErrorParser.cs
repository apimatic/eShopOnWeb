using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;

/// <summary>
/// Reads the error messages out of a Maxio failure response.
/// </summary>
/// <remarks>
/// Maxio shapes its <c>errors</c> member differently per endpoint — an array of strings
/// (<c>{"errors":["Reference: must be unique..."]}</c>), a string, or a map of field to message(s).
/// All three are flattened here so callers get a plain list, and anything unparseable degrades to
/// an empty list rather than masking the original status code with a deserialization error.
/// </remarks>
internal static class MaxioErrorParser
{
    /// <summary>Maximum response length worth parsing; guards against an HTML error page.</summary>
    private const int MaxParsableLength = 64 * 1024;

    public static IReadOnlyList<string> Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body!.Length > MaxParsableLength)
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();
            Flatten(errors, prefix: null, messages);
            return messages;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static void Flatten(JsonElement element, string? prefix, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Add(messages, prefix, element.GetString());
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
        }
    }

    private static void Add(List<string> messages, string? prefix, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        messages.Add(string.IsNullOrWhiteSpace(prefix) ? message! : $"{prefix}: {message}");
    }
}
