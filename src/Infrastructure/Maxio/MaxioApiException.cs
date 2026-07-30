using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio API call returns a non-success status. Carries the HTTP status and any
/// error messages Maxio returned so callers can translate them into meaningful responses.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string rawBody)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string RawBody { get; }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 } ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio API request failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
