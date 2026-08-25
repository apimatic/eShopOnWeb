using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Represents a failed call to the Maxio Advanced Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors)
        : base($"Maxio API request failed with status {(int)statusCode}: {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
}
