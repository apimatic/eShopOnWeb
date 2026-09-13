using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int BalanceInCents { get; set; }
    public int TotalRevenueInCents { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public string? PlanInterval { get; set; }
    public int? PlanIntervalCount { get; set; }
}
