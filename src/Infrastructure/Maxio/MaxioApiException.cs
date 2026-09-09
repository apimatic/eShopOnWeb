using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio Advanced Billing API call returns a non-success status. Carries the HTTP status code
/// and any error messages parsed from the response body (per the spec's error schemas, which express
/// <c>errors</c> as either an array of strings or an object of field messages).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? RawBody { get; }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 }
            ? string.Join("; ", errors)
            : "No error detail was provided.";
        return $"Maxio API request failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
