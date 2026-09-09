using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Raised when a Maxio API call returns a non-success status. Carries the HTTP status and any
/// human-readable messages parsed from the spec's error envelopes.
/// </summary>
public sealed class MaxioApiException : SubscriptionBillingException
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, operation, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio {operation} failed with status {(int)statusCode} ({statusCode}): {detail}.";
    }

    /// <summary>
    /// Extracts human-readable messages from a Maxio error body. The spec uses several shapes: an
    /// <c>{ "errors": [ "..." ] }</c> array, an <c>{ "errors": { "field": "..." } }</c> map, or a bare string.
    /// This tolerates all of them and never throws.
    /// </summary>
    public static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
                return new[] { root.GetString()! };

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("errors", out var errors))
            {
                return Flatten(errors).ToArray();
            }

            // Some endpoints return a bare array or object of messages.
            return Flatten(root).ToArray();
        }
        catch (JsonException)
        {
            // Not JSON (e.g. an HTML gateway error); surface the raw text, trimmed.
            var trimmed = body.Trim();
            return new[] { trimmed.Length > 500 ? trimmed[..500] : trimmed };
        }
    }

    private static IEnumerable<string> Flatten(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s)) yield return s!;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var msg in Flatten(item))
                        yield return msg;
                break;
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    foreach (var msg in Flatten(prop.Value))
                        yield return prop.Value.ValueKind == JsonValueKind.String && prop.Name.Length > 0
                            ? $"{prop.Name}: {msg}"
                            : msg;
                break;
        }
    }
}
