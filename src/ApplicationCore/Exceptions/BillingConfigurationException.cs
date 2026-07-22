using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the integration's configured billing entities do not resolve — a handle that no longer
/// exists, or a component that is not of the expected kind. This points back at the provisioning step,
/// not at the shopper's request, and is never the result of bad user input.
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
