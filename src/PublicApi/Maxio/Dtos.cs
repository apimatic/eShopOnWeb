using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class PlanDto
{
    public int Id { get; set; }
    public int PlanId => Id;
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Monthly price in major currency units.</summary>
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int? BrandId { get; set; }
    public string? Brand { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int SubscriptionId => Id;
    public string State { get; set; } = string.Empty;
    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public long BalanceInCents { get; set; }
    public decimal Balance => BalanceInCents / 100m;
    public int? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
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
    public int? ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<UsageEntryDto> Usages { get; set; } = new();
}

public class UsageDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? FormattedPrice { get; set; }
}
