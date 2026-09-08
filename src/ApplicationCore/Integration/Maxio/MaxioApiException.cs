using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;

/// <summary>
/// Thrown when the Maxio Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when Maxio rejected the call as a duplicate submission (HTTP 409 + uniqueness token).</summary>
    public bool IsDuplicateSubmission { get; }

    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string>? errors, string? message = null)
        : base(message ?? BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
        IsDuplicateSubmission = statusCode == HttpStatusCode.Conflict
            && Errors.Any(e => e.Contains("DuplicateSubmissionError", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string>? errors)
    {
        var detail = errors is { Count: > 0 } ? $" {string.Join("; ", errors)}" : string.Empty;
        return $"Maxio API request failed with status {(int)statusCode} ({statusCode}).{detail}";
    }
}
