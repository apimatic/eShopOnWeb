using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Represents a failed call to the Maxio Billing API.
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
