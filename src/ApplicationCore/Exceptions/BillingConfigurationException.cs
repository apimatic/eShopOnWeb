using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the integration's configuration does not match what exists at the billing
/// provider — a missing setting, or a configured handle that resolves to nothing or to an
/// entity of the wrong shape. The fix is always to correct the configuration or re-seed the
/// provider, never to guess at a substitute entity.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
