using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The state of a shopper's subscription as recorded by the billing system of record (Maxio Advanced Billing).
/// </summary>
public class SubscriptionDetails
{
    public SubscriptionDetails(int maxioSubscriptionId, int maxioCustomerId, string planHandle, string planName,
        long priceInCents, string state, DateTimeOffset? nextBillingAt, DateTimeOffset? createdAt)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingAt = nextBillingAt;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// The subscription id in Maxio Advanced Billing.
    /// </summary>
    public int MaxioSubscriptionId { get; }

    /// <summary>
    /// The Maxio customer id this subscription belongs to.
    /// </summary>
    public int MaxioCustomerId { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    /// <summary>
    /// The recurring amount the subscriber is billed, in integer cents.
    /// </summary>
    public long PriceInCents { get; }

    /// <summary>
    /// Maxio subscription state (e.g. active, trialing, past_due, canceled). See
    /// Maxio "Subscription States" documentation for the full list.
    /// </summary>
    public string State { get; }

    /// <summary>
    /// When the next regularly scheduled billing/assessment will occur, if known.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? CreatedAt { get; }
}
