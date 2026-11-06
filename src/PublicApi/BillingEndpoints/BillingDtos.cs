using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class BrandRefDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Monthly price in the site's main currency, in major units.</summary>
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    /// <summary>The catalog brand the plan belongs to (null for plans not created through this API).</summary>
    public BrandRefDto? Brand { get; set; }
}

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

public class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class SwitchPlanRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    /// <summary>true when the shopper owes nothing on the subscription, false when money is still owed.</summary>
    public bool Paid { get; set; }
    public long BalanceInCents { get; set; }
    public string? Currency { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public int? PlanId { get; set; }
    public long? PlanPriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class UsageRecordDto
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
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageRecordDto> Records { get; set; } = new();
}

public class DailyUsageResponse
{
    public int SubscriptionId { get; set; }
    /// <summary>The day, yyyy-MM-dd, in the billing site's time zone.</summary>
    public string Date { get; set; } = "";
    public decimal TotalQuantity { get; set; }
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    /// <summary>Monthly price in major units of the site's main currency (e.g. 29.99).</summary>
    public decimal? Price { get; set; }
    /// <summary>Id of a brand in the shop's catalog.</summary>
    public int? BrandId { get; set; }
}

public class CurrencyPriceRequest : BaseRequest
{
    /// <summary>ISO 4217 currency code, e.g. EUR. The currency must be enabled on the Maxio site.</summary>
    public string? Currency { get; set; }
    /// <summary>Price in whole units of that currency.</summary>
    public decimal? Price { get; set; }
}

public class CurrencyPriceDto
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
    public string? FormattedPrice { get; set; }
}

public class CurrencyPriceResponse
{
    public int PlanId { get; set; }
    public List<CurrencyPriceDto> CurrencyPrices { get; set; } = new();
}

public class NextBillingDateRequest : BaseRequest
{
    /// <summary>The date to bill on, yyyy-MM-dd.</summary>
    public string? Date { get; set; }
}
