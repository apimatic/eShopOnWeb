using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing integration is not configured (or is misconfigured), so the capability cannot
/// serve requests. The rest of the storefront is unaffected.
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
