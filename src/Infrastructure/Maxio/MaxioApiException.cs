using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns an error response. The error text is
/// parsed from the error models defined in the Maxio OpenAPI spec (error lists, error
/// string maps and attribute-style error objects).
/// </summary>
public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors)
        : base($"Maxio API returned {(int)statusCode} ({statusCode}): {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
