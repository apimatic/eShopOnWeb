using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// The integration's single failure type for Walmart calls. SDK exceptions
/// (<c>ApiException&lt;TError&gt;</c>, deserialization, connection, timeout, auth) are translated
/// to this at the service boundary so endpoints have one type to map to an HTTP status.
/// </summary>
public sealed class WalmartIntegrationException : Exception
{
    public WalmartIntegrationException(string operation, string message, HttpStatusCode? statusCode, Exception? innerException)
        : base(message, innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    /// <summary>The Walmart operation that failed (e.g. <c>UpdateInventoryForSku</c>).</summary>
    public string Operation { get; }

    /// <summary>The provider HTTP status, when the provider answered; null for transport/timeout/auth faults.</summary>
    public HttpStatusCode? StatusCode { get; }
}
