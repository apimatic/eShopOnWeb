using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider could not be reached, or answered in a way the integration
/// cannot act on. The caller should retry later; the request may or may not have been applied.
/// </summary>
public class SubscriptionBillingUnavailableException : SubscriptionBillingException
{
    public SubscriptionBillingUnavailableException(string message) : base(message)
    {
    }

    public SubscriptionBillingUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
