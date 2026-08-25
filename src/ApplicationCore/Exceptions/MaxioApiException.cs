using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success status code.
/// Carries the status code and the error messages from the spec's error models.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string? rawBody = null)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    public string? RawBody { get; }
}
