using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }

    public string State { get; set; } = string.Empty;

    public long? PlanId { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public long? BalanceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public long CustomerId { get; set; }

    public string? CustomerReference { get; set; }

    public string? CustomerEmail { get; set; }
}
