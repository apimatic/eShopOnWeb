using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when this integration is misconfigured against the billing provider — a configured
/// handle that does not resolve, or a component that is not of the metered kind. It signals an
/// operator problem (re-run the sandbox seed), never a customer one, so it is deliberately
/// distinct from <see cref="BillingProviderException"/>.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message)
        : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
