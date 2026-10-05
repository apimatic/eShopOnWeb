using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Billing API returns a non-success response that the
/// client could not recover from. Carries the HTTP status code and the error
/// messages Maxio returned so callers can surface them.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string url, IEnumerable<string> errors)
        : base(BuildMessage(statusCode, url, errors))
    {
        StatusCode = statusCode;
        Url = url;
        Errors = errors?.ToList() ?? new List<string>();
    }

    public MaxioApiException(int statusCode, string url, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Url = url;
        Errors = new List<string> { message };
    }

    public int StatusCode { get; }

    public string Url { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the error list mentions Maxio's "reference must be unique" validation,
    /// used to detect a concurrent customer-create race.
    /// </summary>
    public bool IsDuplicateReferenceError =>
        Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase)
                        && e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(int statusCode, string url, IEnumerable<string> errors)
    {
        var joined = string.Join("; ", errors ?? Enumerable.Empty<string>());
        return $"Maxio Billing API request to '{url}' failed with status {(int)statusCode} ({statusCode}): {joined}";
    }
}