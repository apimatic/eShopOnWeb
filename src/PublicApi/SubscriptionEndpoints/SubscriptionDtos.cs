using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}

public class SubscriptionDetailsDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}

public class SubscriptionCreateResult
{
    public SubscriptionDetailsDto Subscription { get; set; } = new();
    public bool Created { get; set; }
}
