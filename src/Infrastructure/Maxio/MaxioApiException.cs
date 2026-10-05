using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Represents a failed call to the Billing API, carrying the HTTP status code and the
/// error list returned by Maxio so callers can surface meaningful responses.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string? content = null)
        : base(ComposeMessage(statusCode, errors, content))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    private static string ComposeMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors, string? content)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : content;
        return $"Billing API returned {(int)statusCode} ({statusCode}){(string.IsNullOrEmpty(detail) ? "" : $": {detail}")}";
    }

    public static IReadOnlyList<string> ParseErrors(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return root.EnumerateArray()
                    .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
            }

            if (root.ValueKind == System.Text.Json.JsonValueKind.Object &&
                root.TryGetProperty("errors", out var errorsElement))
            {
                switch (errorsElement.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.Array:
                        return errorsElement.EnumerateArray()
                            .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                            .Select(e => e.GetString()!)
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToArray();
                    case System.Text.Json.JsonValueKind.String:
                        return new[] { errorsElement.GetString()! };
                    case System.Text.Json.JsonValueKind.Object:
                        // Maxio sometimes returns {"errors": {"field": ["message", ...]}}
                        return errorsElement.EnumerateObject()
                            .SelectMany(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.Array
                                ? p.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty)
                                : new[] { p.Value.ToString() ?? string.Empty })
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToArray();
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // fall through to raw body
        }

        return new[] { responseBody };
    }
}