using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// The single failure type the Walmart integration presents to the rest of the app. SDK exceptions
/// (API errors, connection failures, timeouts, auth failures, undecodable bodies) are translated to this
/// at the service boundary, carrying a caller-safe message and — where the provider answered — its status.
/// </summary>
public sealed class WalmartIntegrationException : Exception
{
    /// <summary>The provider's HTTP status, when it answered; null for transport/timeout/auth failures.</summary>
    public HttpStatusCode? StatusCode { get; }

    public WalmartIntegrationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public WalmartIntegrationException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
