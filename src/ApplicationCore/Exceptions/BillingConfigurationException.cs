using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider is not configured (or is misconfigured), so the capability cannot serve
/// requests. Surfaced to callers as <c>503 Service Unavailable</c> - the deployment is at fault,
/// not the caller.
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
