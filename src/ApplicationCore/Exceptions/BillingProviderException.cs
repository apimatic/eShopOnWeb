using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider (Maxio) is unreachable or returns an unexpected error that the
/// shopper cannot act on. Surfaced to API callers as a 502 Bad Gateway.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
