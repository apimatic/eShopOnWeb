using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as confirmed by Maxio (plan, price, state, next billing date, ...).
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long ProductPriceInCents { get; set; }

    /// <summary>Plan price in major currency units.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current billing period (the next billing date).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerReference { get; set; }
}
