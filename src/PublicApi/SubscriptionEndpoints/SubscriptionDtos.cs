namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan offered to the shopper.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string BillingPeriod { get; set; } = string.Empty;
    public bool Taxable { get; set; }
    public bool RequiresPaymentMethod { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}

/// <summary>A subscription enrolled for the current user.</summary>
public class MySubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public System.DateTimeOffset? ActivatedAt { get; set; }
    public System.DateTimeOffset? NextBillingAt { get; set; }
}
