using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// A subscription plan (a Maxio Advanced Billing "product"), from the configured product family.
/// </summary>
public record SubscriptionPlan
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = "month";
    public bool RequiresPaymentMethod { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
}
