using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Shared JSON serialization settings for the Maxio Billing API (snake_case, case-insensitive,
/// tolerant of unknown fields).
/// </summary>
public static class MaxioJson
{
    public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static string TryExtractErrors(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "No response body.";
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join("; ", ToStrings(errors)),
                    JsonValueKind.Object => string.Join("; ", FlattenObject(errors)),
                    JsonValueKind.String => errors.GetString() ?? string.Empty,
                    _ => responseBody
                };
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return responseBody;
    }

    private static IEnumerable<string> ToStrings(JsonElement element)
    {
        foreach (var item in element.EnumerateArray())
        {
            yield return item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.GetRawText();
        }
    }

    private static IEnumerable<string> FlattenObject(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in ToStrings(property.Value))
                {
                    yield return $"{property.Name}: {message}";
                }
            }
            else
            {
                yield return $"{property.Name}: {property.Value.GetRawText()}";
            }
        }
    }
}