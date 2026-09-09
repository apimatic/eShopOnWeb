using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an interaction with the external billing system of record fails.
/// Carries the upstream HTTP status (when the failure came from the billing API) so callers can
/// distinguish a bad request we sent (4xx) from an upstream outage (5xx / transport failure).
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int? upstreamStatusCode = null, string? upstreamBody = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
        UpstreamBody = upstreamBody;
    }

    /// <summary>The HTTP status code returned by the billing API, if the failure originated there.</summary>
    public int? UpstreamStatusCode { get; }

    /// <summary>The raw response body returned by the billing API, if any (for diagnostics/logging).</summary>
    public string? UpstreamBody { get; }
}
