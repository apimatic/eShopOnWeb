using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a subscription plan (product) available for subscription.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}

/// <summary>
/// Result of creating a subscription in Maxio.
/// </summary>
public class CreateSubscriptionResult
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public decimal ProductPrice => ProductPriceInCents / 100m;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Represents an existing subscription for a customer.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public decimal ProductPrice => ProductPriceInCents / 100m;
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
