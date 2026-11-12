using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? Brand { get; set; }
}

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public int? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>True when nothing is owed on the subscription, false when money is still owed.</summary>
    public bool Paid { get; set; }

    public decimal BalanceDue { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
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
    public int ComponentId { get; set; }
    public string? ComponentName { get; set; }
    public string? ComponentHandle { get; set; }
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
    public int PlanId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
