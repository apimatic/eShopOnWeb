using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested subscription id does not exist, or does not belong to the calling customer.
/// The same message is used for both cases deliberately, so a customer probing another user's
/// subscription id cannot distinguish "not found" from "not yours" (no enumeration leak).
/// </summary>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription found with id {subscriptionId}")
    {
    }
}
