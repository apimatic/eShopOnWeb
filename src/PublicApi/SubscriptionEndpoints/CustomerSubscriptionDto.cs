using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A customer's subscription surfaced to API clients.</summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public int CustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
}
