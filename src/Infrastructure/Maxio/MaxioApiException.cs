using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio API call returns a non-success status. Carries the HTTP status and any
/// error messages parsed from Maxio's error model (<c>{ "errors": [ ... ] }</c> or the
/// customer variant <c>{ "errors": { "field": "message" } }</c>).
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error details returned";
        return $"Maxio API request failed with status {(int)statusCode} ({statusCode}): {detail}";
    }

    /// <summary>
    /// Best-effort parse of a Maxio error response body into a flat list of messages. Tolerant of
    /// both the array and object-keyed error shapes, and of non-JSON bodies.
    /// </summary>
    public static IReadOnlyList<string> ParseErrors(string? body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors))
            {
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        messages.AddRange(errors.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String)
                            .Select(e => e.GetString()!));
                        break;
                    case JsonValueKind.Object:
                        foreach (var prop in errors.EnumerateObject())
                        {
                            var value = prop.Value.ValueKind == JsonValueKind.String
                                ? prop.Value.GetString()
                                : prop.Value.ToString();
                            messages.Add($"{prop.Name}: {value}");
                        }
                        break;
                    case JsonValueKind.String:
                        messages.Add(errors.GetString()!);
                        break;
                }
            }
        }
        catch (JsonException)
        {
            messages.Add(body.Length > 500 ? body[..500] : body);
        }

        return messages;
    }
}
