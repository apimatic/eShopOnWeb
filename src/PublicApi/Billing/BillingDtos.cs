using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public decimal MonthlyPrice { get; set; }
    public long PriceInCents { get; set; }
    public string? Brand { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public int? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    /// <summary>Only set on the subscription page: true when nothing is owed.</summary>
    public bool? Paid { get; set; }
}

public class UsageItemDto
{
    public long? Id { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class ComponentUsageDto
{
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<UsageItemDto> Usages { get; set; } = new();
}

public class SubscriptionUsageDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
