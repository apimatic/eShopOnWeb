using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = null!;
    public int CustomerId { get; set; }
    public string ProductName { get; set; } = null!;
    public long ProductPriceInCents { get; set; }
    public decimal ProductPriceInDollars => ProductPriceInCents / 100m;
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
