using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Monthly price in major currency units.</summary>
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
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
    /// <summary>true when nothing is owed on the subscription.</summary>
    public bool Paid { get; set; }
    public long BalanceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
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
    public string? ComponentHandle { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<UsageEntryDto> Entries { get; set; } = new();
}

public class UsageDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = "";
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class SwitchPlanRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    /// <summary>Monthly price in major currency units (e.g. 19.99).</summary>
    public decimal? Price { get; set; }
    /// <summary>Id of a brand in the shop's catalog.</summary>
    public int? BrandId { get; set; }
}

public class CurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class NextBillingDateRequest : BaseRequest
{
    /// <summary>yyyy-MM-dd</summary>
    public string? Date { get; set; }
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string? Currency { get; set; }
    public double Price { get; set; }
}
