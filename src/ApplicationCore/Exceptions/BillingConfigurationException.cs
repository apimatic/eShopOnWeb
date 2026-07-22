using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the integration's configuration does not match the provider's state — e.g. a
/// configured product/component handle does not resolve, or the metered component is not of
/// metered kind. Points the operator back at the sandbox seed (UC0).
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}
