using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A local snapshot of a Maxio (Advanced Billing) subscription, tying the
/// eShopOnWeb user to their Maxio customer and subscription. Maxio remains the
/// billing system of record; this row is a durable cross-reference.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public long MaxioCustomerId { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }
    public int PriceInCents { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset? NextBillingDateUtc { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }

    public Subscription(string userId,
        long maxioCustomerId,
        long maxioSubscriptionId,
        string planHandle,
        string planName,
        int priceInCents,
        string state,
        DateTimeOffset? nextBillingDateUtc)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingDateUtc = nextBillingDateUtc;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateState(string state, DateTimeOffset? nextBillingDateUtc)
    {
        State = state;
        NextBillingDateUtc = nextBillingDateUtc;
    }
}
