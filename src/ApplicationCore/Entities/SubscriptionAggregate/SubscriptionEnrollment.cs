using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum EnrollmentStatus
{
    /// <summary>Claimed; the Maxio subscription is being created or its outcome is not yet known.</summary>
    Pending = 0,
    /// <summary>The Maxio subscription exists and its id is recorded.</summary>
    Active = 1
}

/// <summary>
/// One user's enrollment in one plan. The composite key (BuyerId, PlanHandle) is the duplicate-prevention
/// claim: it is inserted before the Maxio call, so a second concurrent Subscribe is refused by the store.
/// </summary>
public class SubscriptionEnrollment
{
    public string BuyerId { get; private set; }
    public string PlanHandle { get; private set; }
    public string Reference { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public SubscriptionEnrollment(string buyerId, string planHandle, string reference, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        PlanHandle = planHandle;
        Reference = reference;
        CreatedAt = createdAt;
        Status = EnrollmentStatus.Pending;
    }

    public void Activate(int maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = EnrollmentStatus.Active;
    }
}
