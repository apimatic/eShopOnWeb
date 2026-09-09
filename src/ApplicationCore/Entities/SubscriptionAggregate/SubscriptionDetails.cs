using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The state of a shopper's recurring subscription (Maxio Advanced Billing Subscription)
/// as confirmed back to the user.
/// </summary>
public class SubscriptionDetails
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodStart { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    /// <summary>
    /// True when the requested subscription already existed (idempotent replay) instead of being created now.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}
