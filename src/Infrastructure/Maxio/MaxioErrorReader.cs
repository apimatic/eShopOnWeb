using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reads the error payloads described by the specification. Maxio returns errors in a few
/// documented shapes - an array of strings (Error-Array-Response), a single string
/// (Single-String-Error-Response), a field/message map (Error-String-Map) or a single
/// "error" string (Single-Error-Response) - so all of them are handled here.
/// </summary>
internal static class MaxioErrorReader
{
    private const int MaxRawBodyLength = 500;

    public static IReadOnlyList<string> Read(string? body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body!);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Truncated(body!);
            }

            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                AppendErrors(messages, errors);
            }

            if (document.RootElement.TryGetProperty("error", out var singleError) &&
                singleError.ValueKind == JsonValueKind.String)
            {
                Append(messages, singleError.GetString());
            }
        }
        catch (JsonException)
        {
            return Truncated(body!);
        }

        return messages.Count > 0 ? messages : Truncated(body!);
    }

    private static void AppendErrors(List<string> messages, JsonElement errors)
    {
        switch (errors.ValueKind)
        {
            case JsonValueKind.String:
                Append(messages, errors.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    Append(messages, item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString());
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in errors.EnumerateObject())
                {
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                    Append(messages, $"{property.Name}: {value}");
                }

                break;
        }
    }

    private static void Append(List<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message!.Trim());
        }
    }

    private static IReadOnlyList<string> Truncated(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
        {
            return new List<string>();
        }

        return new List<string>
        {
            trimmed.Length <= MaxRawBodyLength ? trimmed : trimmed.Substring(0, MaxRawBodyLength) + "..."
        };
    }
}
