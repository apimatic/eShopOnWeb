using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the integration is configured to use a billing entity that does not exist or does
/// not have the expected shape — for example a plan handle that no longer resolves after a sandbox
/// re-seed, or a component that is not of metered kind. The remedy is always to correct the seed or
/// the configuration, never to retry.
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
