using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Parses Maxio error payloads. Per the spec, the <c>errors</c> member may be an array of
/// strings (Error-List-Response) or an object of field → message(s)
/// (Customer-Error-Response); a bare string is also tolerated.
/// </summary>
internal static class MaxioErrorParser
{
    public static IReadOnlyList<string> Parse(string? body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            CollectMessages(errors, messages);
        }
        catch (JsonException)
        {
            // Non-JSON body — leave messages empty; the raw body is preserved by the caller.
        }

        return messages;
    }

    private static void CollectMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Add(messages, element.GetString());
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
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        Add(messages, $"{property.Name}: {property.Value.GetString()}");
                    }
                    else
                    {
                        CollectMessages(property.Value, messages);
                    }
                }
                break;
        }
    }

    private static void Add(List<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message!);
        }
    }
}
