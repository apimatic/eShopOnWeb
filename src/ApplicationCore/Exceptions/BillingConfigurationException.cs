using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the configured billing entities do not match what the integration expects — for
/// example the metered component handle resolving to a non-metered component. Points the operator
/// back at the sandbox seed (UC0).
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}
