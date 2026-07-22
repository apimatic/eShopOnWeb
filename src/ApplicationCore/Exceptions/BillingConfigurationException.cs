using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when configured billing entities do not resolve at the provider, or resolve to something
/// of the wrong shape — for example a metered component handle that maps to a quantity-based
/// component. The fix is always to correct the sandbox seed or the configuration, never to retry.
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
