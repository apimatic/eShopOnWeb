using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the upstream billing provider (Maxio) returns an error we cannot recover
/// from, or is unreachable. Surfaces as HTTP 502 Bad Gateway: the failure is upstream,
/// not in the caller's request.
/// </summary>
public class BillingUpstreamException : Exception
{
    public BillingUpstreamException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
