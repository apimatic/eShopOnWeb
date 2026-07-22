using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a user who already has a live subscription tries to enrol in a different plan.
/// A second enrolment is never created — the customer changes plan instead (UC3).
/// </summary>
public class ActiveSubscriptionExistsException : Exception
{
    public ActiveSubscriptionExistsException(int subscriptionId, string currentPlanHandle, string requestedPlanHandle)
        : base($"This account already has a live subscription (id {subscriptionId}) on plan '{currentPlanHandle}'. " +
               $"Change the existing subscription to '{requestedPlanHandle}' rather than subscribing again.")
    {
        SubscriptionId = subscriptionId;
        CurrentPlanHandle = currentPlanHandle;
        RequestedPlanHandle = requestedPlanHandle;
    }

    public int SubscriptionId { get; }
    public string CurrentPlanHandle { get; }
    public string RequestedPlanHandle { get; }
}
