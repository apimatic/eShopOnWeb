using System;

namespace Microsoft.eShopWeb.PublicApi;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string ProductFamilyName { get; set; } = string.Empty;
}

public class SubscriptionResult
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int CustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string ProductFamilyName { get; set; } = string.Empty;
}
