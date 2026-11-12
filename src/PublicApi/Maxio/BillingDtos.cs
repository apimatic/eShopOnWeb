using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public long PriceInCents { get; set; }
    public int BrandId { get; set; }
    public string Brand { get; set; } = string.Empty;
}

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
    /// <summary>True when the plan list was cut short by a page cap.</summary>
    public bool Truncated { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long BalanceInCents { get; set; }
    /// <summary>true when the shopper owes nothing on the subscription, false when they still owe money.</summary>
    public bool Paid { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
}

public class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}

public class SwitchPlanRequest
{
    public string? PlanHandle { get; set; }
}

public class UsageComponentDto
{
    public int ComponentId { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public bool Truncated { get; set; }
}

public class UsageResponse
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<UsageComponentDto> Components { get; set; } = new();
    public bool Truncated { get; set; }
}

public class CreatePlanRequest
{
    public string? Name { get; set; }
    public decimal? MonthlyPrice { get; set; }
    /// <summary>Name (or id) of a brand in the catalog of the shop.</summary>
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class NextBillingDateRequest
{
    /// <summary>yyyy-MM-dd</summary>
    public string? Date { get; set; }
}
