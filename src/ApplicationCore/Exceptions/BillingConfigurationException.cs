using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing configuration does not match the provider's catalog — for example a
/// configured product or component handle that does not resolve, or a component that resolves to
/// the wrong kind. This always points back at the sandbox seed (plan.md UC0) rather than at a
/// transient provider failure, so it is deliberately distinct from
/// <see cref="BillingProviderException"/>.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
