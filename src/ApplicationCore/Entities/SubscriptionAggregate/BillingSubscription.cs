using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Subscription payload returned by the billing gateway (maps to a Maxio Subscription).
/// </summary>
public sealed class BillingSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ProductHandle { get; init; }
    public string ProductName { get; init; } = string.Empty;
}
