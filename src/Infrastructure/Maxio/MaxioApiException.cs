using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// Carries the HTTP status code and the errors reported by Maxio.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? context = null)
        : base(ComposeMessage(statusCode, errors, context))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    /// <summary>
    /// HTTP status code returned by the Maxio API.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Error messages reported by the Maxio API (may be empty for transport-level failures).
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    public bool IsNotFound => StatusCode == 404;

    private static string ComposeMessage(int statusCode, IReadOnlyList<string> errors, string? context)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : $"HTTP {(System.Net.HttpStatusCode)statusCode}";
        return string.IsNullOrEmpty(context)
            ? $"Maxio API error ({statusCode}): {detail}"
            : $"Maxio API error ({statusCode}) while {context}: {detail}";
    }
}
