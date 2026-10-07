using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription purchased by an eShopOnWeb user, billed through the
/// external billing system of record (Maxio Advanced Billing).
/// </summary>
public class UserSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string UserEmail { get; private set; }
    public long BillingCustomerId { get; private set; }
    public long BillingSubscriptionId { get; private set; }
    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }
    public long PriceInCents { get; private set; }
    public string State { get; private set; }
    public DateTime? NextBillingAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public UserSubscription(string userId, string userEmail, long billingCustomerId,
        long billingSubscriptionId, string planHandle, string planName,
        long priceInCents, string state, DateTime? nextBillingAt)
    {
        var now = DateTime.UtcNow;
        UserId = userId;
        UserEmail = userEmail;
        BillingCustomerId = billingCustomerId;
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingAt = nextBillingAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void Refresh(string state, long priceInCents, DateTime? nextBillingAt)
    {
        State = state;
        PriceInCents = priceInCents;
        NextBillingAt = nextBillingAt;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Points this record at a new billing subscription (used when a
    /// terminal subscription is replaced by a fresh one).
    /// </summary>
    public void RebindTo(long billingSubscriptionId, long priceInCents, string state, DateTime? nextBillingAt)
    {
        BillingSubscriptionId = billingSubscriptionId;
        Refresh(state, priceInCents, nextBillingAt);
    }
}
