using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps an unexpected failure talking to the billing provider (network failure, unmapped error
/// response). Thrown by <see cref="Interfaces.IBillingClient"/> implementations so ApplicationCore
/// and its callers never need to know the provider's own exception types.
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
