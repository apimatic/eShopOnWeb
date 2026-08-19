using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// A shopper's subscription as confirmed by Maxio, the billing system of record.
/// </summary>
public sealed class CustomerSubscription
{
    public int Id { get; init; }
    public required string State { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
