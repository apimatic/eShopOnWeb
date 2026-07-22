using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a plan change is refused by eShopOnWeb's own rules before any provider call (UC3):
/// the target plan is the one the subscription is already on, or the subscription is in a state that
/// does not permit a plan change.
/// </summary>
public class PlanChangeNotAllowedException : Exception
{
    public PlanChangeNotAllowedException(int subscriptionId, string reason)
        : base($"Subscription {subscriptionId} cannot change plan: {reason}")
    {
        SubscriptionId = subscriptionId;
        Reason = reason;
    }

    public int SubscriptionId { get; }

    public string Reason { get; }
}
