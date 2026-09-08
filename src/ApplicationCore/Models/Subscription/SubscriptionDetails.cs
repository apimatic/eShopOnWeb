using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// The state of a subscription as reported by Maxio Advanced Billing
/// (the billing system of record).
/// </summary>
public class SubscriptionDetails
{
    public SubscriptionDetails(long maxioSubscriptionId, long maxioCustomerId, string maxioReference,
        string planHandle, string planName, string state, long priceInCents,
        int interval, string intervalUnit, DateTimeOffset? nextBillingAt, DateTimeOffset? activatedAt)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        MaxioReference = maxioReference;
        PlanHandle = planHandle;
        PlanName = planName;
        State = state;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
    }

    public long MaxioSubscriptionId { get; }
    public long MaxioCustomerId { get; }
    public string MaxioReference { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }
    public string State { get; }
    public long PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public DateTimeOffset? NextBillingAt { get; }
    public DateTimeOffset? ActivatedAt { get; }

    public string FormattedPrice => $"${(PriceInCents / 100m):0.00}";
}
