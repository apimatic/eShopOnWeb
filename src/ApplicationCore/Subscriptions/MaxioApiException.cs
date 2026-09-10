using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when Maxio returns an unsuccessful HTTP response. Carries the status code and any
/// error messages Maxio reported so callers can map them to an appropriate API response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? rawBody = null)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? RawBody { get; }

    /// <summary>
    /// True when Maxio rejected the request as a duplicate. This happens on a 409 from the
    /// duplicate-prevention (uniqueness_token) mechanism, or on a 422 whose error text indicates
    /// the customer <c>reference</c> has already been taken.
    /// </summary>
    public bool IsDuplicate =>
        StatusCode == 409 ||
        (StatusCode == 422 && Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            (e.Contains("taken", StringComparison.OrdinalIgnoreCase) ||
             e.Contains("already", StringComparison.OrdinalIgnoreCase) ||
             e.Contains("used", StringComparison.OrdinalIgnoreCase))));

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 } ? string.Join("; ", errors) : "no details provided";
        return $"Maxio API request failed with status {statusCode}: {detail}";
    }
}
