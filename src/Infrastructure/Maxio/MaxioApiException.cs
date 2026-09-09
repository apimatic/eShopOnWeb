using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// Carries the HTTP status code and the API-reported error messages.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> ApiErrors { get; }

    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string? message = null)
        : base(message ?? BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        ApiErrors = errors;
    }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no details returned";
        return $"Maxio Advanced Billing request failed with HTTP {(int)statusCode} ({statusCode}): {detail}";
    }

    /// <summary>True when Maxio reported a validation conflict (HTTP 422).</summary>
    public bool IsValidationConflict => StatusCode == HttpStatusCode.UnprocessableEntity;
}
