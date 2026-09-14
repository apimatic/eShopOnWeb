using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when Maxio's API returns a non-success response for an operation that is not
/// handled as a normal "not found" outcome.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status code returned by the Maxio API.</summary>
    public int StatusCode { get; }

    /// <summary>The raw error messages reported by Maxio (when the error body carried any).</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Composes the most useful single message from a Maxio error body that can be either a
    /// list of strings, a map of field name to messages, or a plain string.
    /// </summary>
    public static string BuildMessage(int statusCode, string? body, IReadOnlyList<string>? parsedErrors)
    {
        if (parsedErrors is { Count: > 0 })
        {
            return string.Join(" ", parsedErrors);
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            return body.Trim();
        }

        return $"The Maxio API returned HTTP {statusCode}.";
    }

    /// <summary>
    /// Best-effort interpretation of a Maxio JSON error body whose "errors" member can be an
    /// array of strings, a map of field => messages, or absent entirely.
    /// </summary>
    public static IReadOnlyList<string> ParseErrors(JsonElement? errorsElement)
    {
        if (errorsElement is not { } element
            || element.ValueKind == JsonValueKind.Undefined
            || element.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty)
                .Where(s => s.Length > 0)
                .ToList();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var messages = new List<string>();
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    messages.Add($"{property.Name}: {property.Value.GetString()}");
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            messages.Add($"{property.Name}: {item.GetString()}");
                        }
                    }
                }
            }

            return messages;
        }

        return Array.Empty<string>();
    }
}
