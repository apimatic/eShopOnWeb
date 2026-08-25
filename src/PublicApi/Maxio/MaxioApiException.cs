using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a non-success response from the Maxio Advanced Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string rawBody)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string RawBody { get; }
}
