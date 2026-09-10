using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the external billing system of record cannot be reached or returns an
/// unexpected error. Signals an upstream/integration failure (mapped to HTTP 502) rather
/// than a fault in the caller's request.
/// </summary>
public class BillingGatewayException : Exception
{
    public BillingGatewayException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
