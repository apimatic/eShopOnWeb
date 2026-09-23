using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscription belonging to the current buyer, projected from a Maxio subscription.
/// </summary>
public record CustomerSubscriptionInfo
{
    public int SubscriptionId { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public long PriceInCents { get; init; }
    public required string FormattedPrice { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public string? Reference { get; init; }
}
