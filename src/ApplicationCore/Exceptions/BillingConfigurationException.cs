using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The subscription-billing integration is missing or has invalid configuration, so the capability
/// cannot be served. Surfaced to callers as "service unavailable" - it is an operator problem,
/// never a caller problem.
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
