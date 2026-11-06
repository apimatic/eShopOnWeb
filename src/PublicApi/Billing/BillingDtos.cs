using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? BrandId { get; set; }
    public string? Brand { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public bool Paused { get; set; }

    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public decimal BalanceDue { get; set; }
    public int? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class UsageEntryDto
{
    public long? Id { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}

public class ComponentUsageDto
{
    public int? ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<UsageEntryDto> Entries { get; set; } = new();
}

public class DailyUsageDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = "";
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Price { get; set; }
}
