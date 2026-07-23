using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the integration is misconfigured against the billing provider — a configured handle
/// does not resolve, or resolves to an entity of the wrong shape. Distinct from
/// <see cref="BillingProviderException"/> because the fix is to correct the seed or the settings,
/// not to retry the call.
/// </summary>
public class BillingConfigurationException : BillingProviderException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
