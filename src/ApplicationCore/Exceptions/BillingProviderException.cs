using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An unexpected failure from the billing provider (network, 5xx, or an error shape the client
/// could not interpret). Never rolls back or blocks eShopOnWeb's own order lifecycle (§2.5).
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
