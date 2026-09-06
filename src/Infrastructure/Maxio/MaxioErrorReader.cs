using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Extracts human-readable error messages from an Advanced Billing error body.
/// </summary>
/// <remarks>
/// The API is not uniform here: most endpoints return <c>{"errors": ["..."]}</c>, some return
/// <c>{"errors": {"field": ["..."]}}</c>, a few return <c>{"error": "..."}</c>, and 404s come back
/// with an empty body. All four are handled so a caller always gets something actionable.
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
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new[] { Truncate(body) };
            }

            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors))
            {
                AppendMessages(errors, prefix: null, messages);
            }

            if (root.TryGetProperty("error", out var singleError) && singleError.ValueKind == JsonValueKind.String)
            {
                var value = singleError.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value!);
                }
            }

            return messages.Count > 0 ? messages : new[] { Truncate(body) };
        }
        catch (JsonException)
        {
            return new[] { Truncate(body) };
        }
    }

    private static void AppendMessages(JsonElement element, string? prefix, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(prefix is null ? text! : $"{prefix}: {text}");
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AppendMessages(item, prefix, messages);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AppendMessages(property.Value, property.Name, messages);
                }

                break;
        }
    }

    private static string Truncate(string body)
    {
        body = body.Trim();
        return body.Length <= MaxRawBodyLength ? body : body.Substring(0, MaxRawBodyLength) + "...";
    }
}
