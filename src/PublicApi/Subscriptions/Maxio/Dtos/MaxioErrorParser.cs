using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio.Dtos;

/// <summary>
/// Parses Maxio error payloads into a flat list of human readable messages.
///
/// The OpenAPI specification models errors in a few related shapes
/// (see components/schemas/errors/*): a bare list of strings under "errors",
/// an object map of field -> message under "errors", or a single "error" string.
/// All of them are flattened here so callers can rely on one contract.
/// </summary>
public static class MaxioErrorParser
{
    public static IReadOnlyList<string> Parse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new[] { responseBody };
            }

            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors))
            {
                CollectErrors(errors, messages);
            }
            else if (root.TryGetProperty("error", out var error))
            {
                CollectErrors(error, messages);
            }

            return messages.Count > 0 ? messages : new[] { responseBody };
        }
        catch (JsonException)
        {
            return new[] { responseBody };
        }
    }

    private static void CollectErrors(JsonElement errors, List<string> messages)
    {
        switch (errors.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    messages.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.ToString());
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in errors.EnumerateObject())
                {
                    var value = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Array => string.Join(", ", property.Value.EnumerateArray()
                            .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())),
                        _ => property.Value.ToString()
                    };
                    messages.Add($"{property.Name}: {value}");
                }
                break;

            case JsonValueKind.String:
                messages.Add(errors.GetString()!);
                break;

            default:
                messages.Add(errors.ToString());
                break;
        }
    }
}
