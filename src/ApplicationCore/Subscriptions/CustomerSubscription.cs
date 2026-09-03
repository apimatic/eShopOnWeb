using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A customer's subscription to a plan, as reported by the billing system.
/// <see cref="NextBillingDate"/> is the date the customer is next billed.
/// </summary>
public record CustomerSubscription
{
    public int? Id { get; init; }
    public string? State { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public long? PriceInCents { get; init; }
    public string? FormattedPrice { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string? CustomerReference { get; init; }
    public int? CustomerId { get; init; }
}
