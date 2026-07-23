using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan change is not a change at all — the subscription is already on the target
/// plan. Rejected as a no-op before any provider call is made.
/// </summary>
public class InvalidPlanChangeException : Exception
{
    public InvalidPlanChangeException(int subscriptionId, string planHandle)
        : base($"Subscription {subscriptionId} is already on plan {planHandle}.")
    {
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
    }

    public int SubscriptionId { get; }

    public string PlanHandle { get; }
}
