using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success status code.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Reads a Maxio error payload into an exception. Maxio returns errors either as an
    /// <c>{"errors": [...]}</c> array or as a single <c>{"error": "..."}</c> string.
    /// </summary>
    public static MaxioApiException FromPayload(int statusCode, string payload)
    {
        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in errorsElement.EnumerateArray())
                    {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            errors.Add(item.GetString()!);
                        }
                        else
                        {
                            errors.Add(item.GetRawText());
                        }
                    }
                }
                else if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    errors.Add(errorElement.GetString()!);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall through and surface the raw payload as the message.
            }
        }

        var message = errors.Count > 0
            ? string.Join(" ", errors)
            : string.IsNullOrWhiteSpace(payload)
                ? $"Maxio Advanced Billing returned HTTP {(int)statusCode}."
                : payload;

        return new MaxioApiException(statusCode, message, errors);
    }
}
