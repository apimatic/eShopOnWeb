using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
