using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by <see cref="Interfaces.IBillingClient"/> implementations for any billing-provider
/// failure — an error response from the provider, or a connectivity failure reaching it. This is
/// the single failure shape the rest of the application sees from billing operations.
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
