using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when Maxio Advanced Billing returns a non-success response. Carries the
/// HTTP status code and the error messages from Maxio's error model.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? responseBody = null)
        : base($"Maxio API request failed with status {statusCode}: {(errors.Count > 0 ? string.Join(" ", errors) : responseBody ?? "(no body)")}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
