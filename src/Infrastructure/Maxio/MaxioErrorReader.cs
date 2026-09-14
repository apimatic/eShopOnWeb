using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reads the <c>errors</c> payload of a failed Maxio response. The API returns it either as an array
/// of strings or as an object keyed by field name, and error bodies are never guaranteed to be JSON
/// at all — so this degrades to the raw body rather than throwing while handling a failure.
/// </summary>
internal static class MaxioErrorReader
{
    private const int MaxRawBodyLength = 500;

    public static async Task<IReadOnlyList<string>> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ObjectDisposedException)
        {
            return Array.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                Flatten(errors, messages);
                if (messages.Count > 0)
                {
                    return messages;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw body.
        }

        return new[] { Truncate(body) };
    }

    private static void Flatten(JsonElement element, ICollection<string> messages)
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
                    Flatten(item, messages);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in Describe(property))
                    {
                        messages.Add(message);
                    }
                }

                break;

            default:
                messages.Add(element.ToString());
                break;
        }
    }

    private static IEnumerable<string> Describe(JsonProperty property)
    {
        var nested = new List<string>();
        Flatten(property.Value, nested);
        foreach (var message in nested)
        {
            yield return $"{property.Name}: {message}";
        }
    }

    private static string Truncate(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= MaxRawBodyLength ? trimmed : trimmed.Substring(0, MaxRawBodyLength) + "…";
    }
}
