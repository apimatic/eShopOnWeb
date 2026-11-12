using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Monthly price in major units of <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string? Currency { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int BrandId { get; set; }
    public string Brand { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    /// <summary>true when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public decimal BalanceDue { get; set; }
    public string? Currency { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public int? PlanId { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public class UsageEntryDto
{
    public long? Id { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class ComponentUsageDto
{
    public int ComponentId { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public string? Kind { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageEntryDto> Usages { get; set; } = new();
}

public class UsageDayDto
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

public class NextBillingDto
{
    public int SubscriptionId { get; set; }
    public string NextBillingDate { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
}
