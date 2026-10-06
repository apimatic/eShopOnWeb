using System;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public long CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long BalanceInCents { get; set; }
    public decimal Balance { get; set; }
    public string? PaymentCollectionMethod { get; set; }

    public static SubscriptionDto From(MaxioSubscription sub) => new()
    {
        SubscriptionId = sub.Id,
        CustomerId = sub.CustomerId,
        State = sub.State,
        PlanHandle = sub.PlanHandle,
        PlanName = sub.PlanName,
        PriceInCents = sub.PriceInCents,
        Price = sub.PriceInCents / 100m,
        Interval = sub.Interval,
        IntervalUnit = sub.IntervalUnit,
        NextBillingDate = sub.CurrentPeriodEndsAt ?? sub.NextAssessmentAt,
        NextAssessmentAt = sub.NextAssessmentAt,
        ActivatedAt = sub.ActivatedAt,
        CreatedAt = sub.CreatedAt,
        BalanceInCents = sub.BalanceInCents,
        Balance = sub.BalanceInCents / 100m,
        PaymentCollectionMethod = sub.PaymentCollectionMethod
    };
}
