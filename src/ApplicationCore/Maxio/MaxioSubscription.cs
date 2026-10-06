using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Subscription data as held by Maxio Advanced Billing (the billing system of record).
/// </summary>
public record MaxioSubscription(
    long SubscriptionId,
    string Reference,
    string State,
    long CustomerId,
    long ProductId,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string Currency,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CanceledAt,
    string PaymentCollectionMethod)
{
    public bool IsLive => State is "active" or "trial" or "grandfathered" or "paused" or "pending";
}
