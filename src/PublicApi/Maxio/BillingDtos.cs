using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A plan a shopper can subscribe to.</summary>
public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Price per billing interval, in the site's main currency.</summary>
    public decimal Price { get; set; }
    public string? Interval { get; set; }
    public int? BrandId { get; set; }
    public string? Brand { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public int? PlanId { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public decimal Balance { get; set; }
    /// <summary>True when the shopper owes nothing on the subscription, false when they still owe money on it.</summary>
    public bool Paid { get; set; }
}

public class UsageComponentDto
{
    public int ComponentId { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public int Records { get; set; }
}

public class UsageDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<UsageComponentDto> Components { get; set; } = new();
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public int PricePointId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
