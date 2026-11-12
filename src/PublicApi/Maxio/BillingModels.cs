using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Monthly price in the site's currency (major units).</summary>
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
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
    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public decimal Balance { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public class UsageComponentDto
{
    public int ComponentId { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
}

public class UsageDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = "";
    public decimal TotalQuantity { get; set; }
    public List<UsageComponentDto> Components { get; set; } = new();
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Price { get; set; }
}
