using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscribe was requested for a user who already holds a live subscription on a different plan.
/// <para>
/// Re-subscribing to the plan the customer is already on is not an error — it returns the existing
/// subscription (UC1: "never create a second enrollment"). This exception covers only the case
/// where honouring the request would mean a second, conflicting enrolment; changing plans is done
/// through the plan-change flow (UC3) so the customer sees the proration first.
/// </para>
/// </summary>
public class DuplicateSubscriptionException : Exception
{
    public DuplicateSubscriptionException(int existingSubscriptionId, string existingPlanHandle, string requestedPlanHandle)
        : base($"This account already has an active subscription (id {existingSubscriptionId}) on plan " +
               $"'{existingPlanHandle}'. Change plans instead of subscribing to '{requestedPlanHandle}' again.")
    {
        ExistingSubscriptionId = existingSubscriptionId;
        ExistingPlanHandle = existingPlanHandle;
        RequestedPlanHandle = requestedPlanHandle;
    }

    public int ExistingSubscriptionId { get; }

    public string ExistingPlanHandle { get; }

    public string RequestedPlanHandle { get; }
}
