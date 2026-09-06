using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system could not be reached, or answered with a failure eShopOnWeb cannot act on
/// (authentication failure, server error, exhausted retries). Surfaced to callers as a bad gateway.
/// </summary>
public class BillingGatewayException : Exception
{
    public BillingGatewayException(string message, int? upstreamStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
    }

    /// <summary>HTTP status returned by the billing system, when the call completed.</summary>
    public int? UpstreamStatusCode { get; }
}
