using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int PlanId { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public decimal MonthlyPrice { get; set; }
    public string? Currency { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    /// <summary>Amount still owed (0 when paid).</summary>
    public decimal BalanceDue { get; set; }
    public string? Currency { get; set; }
    public int? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? PlanPrice { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateOnly? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class UsageEntryDto
{
    public long? UsageId { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}

public class ComponentUsageDto
{
    public int ComponentId { get; set; }
    public string? ComponentName { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageEntryDto> Usages { get; set; } = new();
}

public class UsageDto
{
    public int SubscriptionId { get; set; }
    public DateOnly Date { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string? Currency { get; set; }
    public decimal Price { get; set; }
}
