using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a non-success response from the Maxio Advanced Billing API.
/// Error shapes follow the spec's Error-List-Response ({"errors": [...]}) and
/// Customer-Error-Response ({"errors": {...}}) models.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
    public string RawBody { get; }

    public MaxioApiException(HttpStatusCode statusCode, string rawBody, IReadOnlyList<string> errors)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        RawBody = rawBody;
        Errors = errors;
    }

    public static MaxioApiException Create(HttpStatusCode statusCode, string rawBody)
    {
        return new MaxioApiException(statusCode, rawBody, ParseErrors(rawBody));
    }

    private static IReadOnlyList<string> ParseErrors(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return new[] { "Empty error response body." };
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errors.EnumerateArray())
                        {
                            messages.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText());
                        }
                        break;
                    case JsonValueKind.Object:
                        foreach (var prop in errors.EnumerateObject())
                        {
                            var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString()! : prop.Value.GetRawText();
                            messages.Add($"{prop.Name}: {value}");
                        }
                        break;
                    case JsonValueKind.String:
                        messages.Add(errors.GetString()!);
                        break;
                }

                if (messages.Count > 0)
                {
                    return messages;
                }
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return new[] { rawBody };
    }
}
