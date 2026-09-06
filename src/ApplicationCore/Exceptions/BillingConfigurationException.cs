using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing integration is not usable because required configuration is missing or invalid.
/// Surfaced as 503 so an unconfigured deployment is obviously an operator problem, not a caller one.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}
