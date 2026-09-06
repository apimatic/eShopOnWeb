using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// JSON handling for the Maxio API, which uses snake_case property names throughout the
/// specification.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// Pulls the human readable messages out of a Maxio error body. The specification models errors
    /// in several shapes: <c>Error-List-Response</c> and <c>Error-Array-Response</c> carry
    /// <c>errors</c> as an array of strings, <c>Customer-Error-Response</c> may instead carry an
    /// object keyed by field name, and <c>Single-Error-Response</c> carries a single <c>error</c>
    /// string. Anything unrecognised yields no messages rather than an exception.
    /// </summary>
    public static IReadOnlyList<string> ReadErrorMessages(string? body)
    {
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return messages;
            }

            if (root.TryGetProperty("errors", out var errors))
            {
                CollectMessages(errors, messages);
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                var message = error.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    messages.Add(message!);
                }
            }
        }
        catch (JsonException)
        {
            // A non-JSON error body (an HTML error page, for instance) carries no structured messages.
        }

        return messages;
    }

    private static void CollectMessages(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value!);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMessages(item, messages);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in Flatten(property))
                    {
                        messages.Add(message);
                    }
                }

                break;
        }
    }

    private static IEnumerable<string> Flatten(JsonProperty property)
    {
        var nested = new List<string>();
        CollectMessages(property.Value, nested);

        foreach (var message in nested)
        {
            yield return $"{property.Name}: {message}";
        }
    }
}
