using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success status code.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {message}")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
