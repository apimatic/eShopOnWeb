using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio API. Parses the spec's error shapes
/// (<c>{ "errors": [ ... ] }</c>, <c>{ "errors": { "customer": "..." } }</c>, or a plain string body).
/// </summary>
public sealed class MaxioApiException : Exception
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
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail";
        return $"Maxio API returned {(int)statusCode} {statusCode}: {detail}";
    }

    /// <summary>Extracts human-readable error messages from a raw error response body.</summary>
    public static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("errors", out var errors))
            {
                return ExtractMessages(errors);
            }

            if (root.ValueKind == JsonValueKind.String)
            {
                var s = root.GetString();
                return string.IsNullOrWhiteSpace(s) ? Array.Empty<string>() : new[] { s };
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw body.
        }

        return new[] { body.Trim() };
    }

    private static IReadOnlyList<string> ExtractMessages(JsonElement errors)
    {
        switch (errors.ValueKind)
        {
            case JsonValueKind.Array:
                return errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList();

            case JsonValueKind.Object:
                return errors.EnumerateObject()
                    .Select(p => p.Value.ValueKind == JsonValueKind.String
                        ? $"{p.Name}: {p.Value.GetString()}"
                        : $"{p.Name}: {p.Value}")
                    .ToList();

            case JsonValueKind.String:
                var single = errors.GetString();
                return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single! };

            default:
                return Array.Empty<string>();
        }
    }
}
