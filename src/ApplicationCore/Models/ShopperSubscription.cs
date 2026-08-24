using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded in the billing system (Maxio).
/// </summary>
public sealed record ShopperSubscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}
