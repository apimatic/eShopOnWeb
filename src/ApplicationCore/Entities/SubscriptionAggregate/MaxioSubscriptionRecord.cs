using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local mirror of a Maxio Advanced Billing subscription, keyed by buyer.
/// Used as the fast-path cache for idempotent subscribes and for the
/// "my subscriptions" view; Maxio remains the system of record.
/// </summary>
public class MaxioSubscriptionRecord : BaseEntity, IAggregateRoot
{
    public MaxioSubscriptionRecord(string buyerId, long maxioCustomerId, long maxioSubscriptionId,
        string maxioReference, string planHandle, string planName, string state, long priceInCents,
        int interval, string intervalUnit, DateTimeOffset? nextBillingAt, DateTimeOffset? activatedAt)
    {
        BuyerId = buyerId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioReference = maxioReference;
        PlanHandle = planHandle;
        PlanName = planName;
        State = state;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

#pragma warning disable CS8618 // Required by EF
    private MaxioSubscriptionRecord() { }
#pragma warning restore CS8618

    public string BuyerId { get; private set; }
    public long MaxioCustomerId { get; private set; }

    /// <summary>
    /// The subscription id assigned by Maxio Advanced Billing.
    /// </summary>
    public long MaxioSubscriptionId { get; private set; }

    /// <summary>
    /// The application-provided reference used to create the subscription in Maxio.
    /// </summary>
    public string MaxioReference { get; private set; }

    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }
    public string State { get; private set; }
    public long PriceInCents { get; private set; }
    public int Interval { get; private set; }
    public string IntervalUnit { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateFrom(long maxioCustomerId, string state, long priceInCents,
        DateTimeOffset? nextBillingAt, string planName)
    {
        MaxioCustomerId = maxioCustomerId;
        State = state;
        PriceInCents = priceInCents;
        NextBillingAt = nextBillingAt;
        PlanName = planName;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
