using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Pulls the human-readable messages out of a Maxio error body.
/// </summary>
/// <remarks>
/// Maxio reports failures in a few shapes depending on the endpoint:
/// <c>{"errors":["Reference: must be unique - that value has been taken."]}</c>,
/// <c>{"errors":{"product_handle":["is not valid"]}}</c> and <c>{"error":"..."}</c>.
/// All three are handled; anything else falls back to an empty list and the caller
/// reports the status code on its own.
/// </remarks>
internal static class MaxioErrorReader
{
    public static IReadOnlyList<string> Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body!);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors))
            {
                CollectMessages(errors, messages);
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                var text = error.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(text!);
                }
            }

            return messages;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// True when the only thing wrong with the request was that the reference we sent is
    /// already in use, e.g. "Reference: must be unique - that value has been taken.".
    /// </summary>
    public static bool IsDuplicateReference(IReadOnlyList<string> messages)
    {
        foreach (var message in messages)
        {
            if (message.Contains("reference", StringComparison.OrdinalIgnoreCase)
                && message.Contains("unique", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(text!);
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
                    // Field-scoped errors arrive as {"field": ["message", ...]}.
                    var before = messages.Count;
                    CollectMessages(property.Value, messages);
                    for (var i = before; i < messages.Count; i++)
                    {
                        messages[i] = $"{property.Name}: {messages[i]}";
                    }
                }

                break;
        }
    }
}
