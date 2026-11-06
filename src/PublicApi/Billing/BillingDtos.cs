using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ProductFamily { get; set; }
    /// <summary>The catalog brand the plan belongs to (null when its product family does not map to a catalog brand).</summary>
    public string? Brand { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public PlanDto? Plan { get; set; }

    /// <summary>true when the shopper owes nothing on the subscription, false when money is still owed.</summary>
    public bool Paid { get; set; }
    public decimal AmountOwed { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class UsageEntryDto
{
    public long Id { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
}

public class ComponentUsageDto
{
    public int ComponentId { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<UsageEntryDto> Entries { get; set; } = new();
}

public class UsageDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CurrencyPriceDto
{
    public string? Currency { get; set; }
    public decimal Price { get; set; }
}

public class CurrencyPricesDto
{
    public int PlanId { get; set; }
    public List<CurrencyPriceDto> CurrencyPrices { get; set; } = new();
}

public class NextBillingDateDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
}
