using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A plan change was rejected before any provider call was made — because it would be a no-op (the
/// subscription is already on the requested plan), or because the subscription is not in a state
/// that permits a plan change (UC3).
/// </summary>
public class PlanChangeNotAllowedException : Exception
{
    public PlanChangeNotAllowedException(int subscriptionId, string reason)
        : base($"Cannot change the plan of subscription {subscriptionId}: {reason}")
    {
        SubscriptionId = subscriptionId;
        Reason = reason;
    }

    public int SubscriptionId { get; }

    /// <summary>Why the change was refused, phrased for display to the customer.</summary>
    public string Reason { get; }

    public static PlanChangeNotAllowedException SamePlan(int subscriptionId, string planHandle) =>
        new(subscriptionId, $"it is already on plan '{planHandle}'.");

    public static PlanChangeNotAllowedException WrongState(int subscriptionId, object currentState) =>
        new(subscriptionId, $"it is {currentState}. Reactivate the subscription before changing plans.");
}
