using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the integration is configured to use a billing entity that does not exist on the
/// provider, or exists in the wrong shape (e.g. the configured usage component is not metered).
/// This is a setup problem — the operator must correct the seed (UC0) rather than retry.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
