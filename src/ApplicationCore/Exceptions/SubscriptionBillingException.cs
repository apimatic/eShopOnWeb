using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription-billing operation cannot be completed because of an error in the
/// billing system of record (Maxio). Endpoints translate this into a Bad Gateway response.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message) : base(message)
    {
    }

    public SubscriptionBillingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
