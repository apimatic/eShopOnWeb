using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A shopper's subscription as reported by Maxio, the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
}
