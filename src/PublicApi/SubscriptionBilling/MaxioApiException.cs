using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns an error response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string? responseBody, IReadOnlyList<string>? errors)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors ?? Array.Empty<string>();
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsReferenceConflict =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            (e.Contains("unique", StringComparison.OrdinalIgnoreCase) || e.Contains("taken", StringComparison.OrdinalIgnoreCase)));

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string>? errors)
    {
        if (errors != null && errors.Count > 0)
        {
            return $"Maxio Advanced Billing returned HTTP {(int)statusCode}: {string.Join(" ", errors)}";
        }

        return $"Maxio Advanced Billing returned HTTP {(int)statusCode}.";
    }

    /// <summary>
    /// Best-effort extraction of the "errors" payload Maxio returns on error responses.
    /// The payload can be either a JSON array of strings or a single string.
    /// </summary>
    public static IReadOnlyList<string>? TryParseErrors(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return null;
            }

            if (errorsElement.ValueKind == JsonValueKind.Array)
            {
                return errorsElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => s.Length > 0)
                    .ToList();
            }

            if (errorsElement.ValueKind == JsonValueKind.String)
            {
                var message = errorsElement.GetString();
                return string.IsNullOrWhiteSpace(message) ? null : new List<string> { message };
            }
        }
        catch (JsonException)
        {
            // Fall through; a missing/opaque error body is reported generically.
        }

        return null;
    }
}
