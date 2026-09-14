using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio (Advanced Billing) API returns an error response, or when its
/// response cannot be understood. Carries the HTTP status and any error messages Maxio
/// returned so callers can surface a meaningful problem detail.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, Exception? innerException = null)
        : base(BuildMessage(statusCode, errors), innerException)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public MaxioApiException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = 0;
        Errors = Array.Empty<string>();
    }

    /// <summary>HTTP status returned by Maxio (0 when the failure was not an HTTP response).</summary>
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 }
            ? string.Join("; ", errors)
            : "no additional detail";
        return $"Maxio API request failed with status {statusCode}: {detail}";
    }
}
