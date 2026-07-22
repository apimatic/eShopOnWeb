using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested subscription does not exist, or does not belong to the requesting user.
/// <para>
/// Both cases deliberately produce the same error so that a customer cannot probe for the existence
/// of another customer's subscription by comparing responses.
/// </para>
/// </summary>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription found with id {subscriptionId}")
    {
    }
}
