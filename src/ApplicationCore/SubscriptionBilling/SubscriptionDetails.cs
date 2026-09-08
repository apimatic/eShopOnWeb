using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A snapshot of a subscription as reported by the billing system of record.
/// </summary>
public sealed class SubscriptionDetails
{
    public long Id { get; init; }

    public string State { get; init; } = string.Empty;

    public string? PlanHandle { get; init; }

    public string PlanName { get; init; } = string.Empty;

    public long? ProductPriceInCents { get; init; }

    public long? BalanceInCents { get; init; }

    public long? TotalRevenueInCents { get; init; }

    public string? Currency { get; init; }

    public string? PaymentCollectionMethod { get; init; }

    public bool? CancelAtEndOfPeriod { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
