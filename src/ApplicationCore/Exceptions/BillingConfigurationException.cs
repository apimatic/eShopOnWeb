using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the integration is configured to use a billing entity that does not exist or does
/// not have the expected shape — for example a product handle that no longer resolves after the
/// sandbox was re-seeded, or a component that is not of metered kind.
/// </summary>
/// <remarks>
/// Distinct from <see cref="BillingProviderException"/> on purpose: this one means "fix the seed or
/// the configuration", not "the provider call failed".
/// </remarks>
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
