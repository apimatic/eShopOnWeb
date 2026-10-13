using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the Walmart integration boundary raises. It carries a caller-safe
/// message and, where the provider answered, the caller-facing HTTP status the endpoint should
/// surface. Never carries a secret, token, or raw SDK type name.
/// </summary>
public class WalmartIntegrationException : Exception
{
    public WalmartIntegrationException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The HTTP status the caller of the eShop endpoint should see for this failure. A provider
    /// 4xx the caller can act on is surfaced as-is; our-credential / quota / transport / unknown
    /// failures are surfaced as 5xx.
    /// </summary>
    public HttpStatusCode StatusCode { get; }
}
