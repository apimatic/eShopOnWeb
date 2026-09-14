using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// JSON conventions shared by every Maxio call. Maxio speaks snake_case, so the naming policy is
/// applied once here instead of decorating each contract property.
/// </summary>
public static class MaxioSerialization
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Decodes an error body into flat messages. Maxio uses several error shapes across the
    /// specification - <c>Error-List-Response</c> (<c>{"errors": ["..."]}</c>),
    /// <c>Customer-Error</c> (<c>{"errors": {"customer": "..."}}</c>),
    /// <c>Single-String-Error-Response</c> (<c>{"errors": "..."}</c>),
    /// <c>Single-Error-Response</c> (<c>{"error": "..."}</c>) - and a few endpoints return a bare
    /// string. All of them are handled so the caller always gets usable messages.
    /// </summary>
    public static IReadOnlyList<string> ParseErrors(string? body)
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
                    CollectFrom(root, messages);
                    break;
                case JsonValueKind.Object:
                    if (root.TryGetProperty("errors", out var errors))
                    {
                        CollectFrom(errors, messages);
                    }

                    if (root.TryGetProperty("error", out var singleError))
                    {
                        CollectFrom(singleError, messages);
                    }

                    if (messages.Count == 0)
                    {
                        AddIfPresent(messages, Truncate(body));
                    }

                    break;
                default:
                    AddIfPresent(messages, Truncate(body));
                    break;
            }

            return messages;
        }
        catch (JsonException)
        {
            return new[] { Truncate(body!) };
        }
    }

    private static void CollectFrom(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddIfPresent(messages, element.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectFrom(item, messages);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        AddIfPresent(messages, $"{property.Name}: {property.Value.GetString()}");
                    }
                    else
                    {
                        CollectFrom(property.Value, messages);
                    }
                }

                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                AddIfPresent(messages, element.ToString());
                break;
        }
    }

    private static void AddIfPresent(List<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message!.Trim());
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value.Substring(0, 500) + "...";
}
