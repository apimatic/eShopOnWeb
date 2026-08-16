using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when a Maxio API call returns a non-success status code. Carries the HTTP status
/// and any error messages parsed from the response body (per the spec's error schemas).
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyCollection<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyCollection<string> Errors { get; }

    public string? RawBody { get; }

    /// <summary>True for client-side validation failures (HTTP 422), which are safe to surface to callers.</summary>
    public bool IsValidationError => (int)StatusCode == 422;

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyCollection<string> errors)
    {
        var detail = errors is { Count: > 0 }
            ? string.Join("; ", errors)
            : "no error detail provided";
        return $"Maxio API request failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
