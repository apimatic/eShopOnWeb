using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Wire representation of a customer's subscription.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public long CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;

    /// <summary>True when a subscribe request returned an existing subscription (idempotent replay).</summary>
    public bool AlreadyExisted { get; set; }
}
