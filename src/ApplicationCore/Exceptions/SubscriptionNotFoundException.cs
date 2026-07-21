using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription id does not exist, or does not belong to the requesting customer.
/// The two cases are deliberately not distinguished to a non-admin caller, to avoid leaking the
/// existence of another customer's subscription id.
/// </summary>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription found with id {subscriptionId}")
    {
    }
}
