using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public decimal MonthlyPrice { get; set; }
}

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
    /// <summary>True when the plan catalog was too large to read completely.</summary>
    public bool Truncated { get; set; }
}

public class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}

public class SwitchPlanRequest
{
    public string? PlanHandle { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public decimal BalanceDue { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class UsageEntryDto
{
    public long? Id { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
}

public class ComponentUsageDto
{
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageEntryDto> Entries { get; set; } = new();
    public bool Truncated { get; set; }
}

public class UsageResponse
{
    public string Date { get; set; } = string.Empty;
    public List<ComponentUsageDto> Components { get; set; } = new();
    public bool Truncated { get; set; }
}

public class CreatePlanRequest
{
    public string? Name { get; set; }
    public decimal MonthlyPrice { get; set; }
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal Price { get; set; }
}

public class CurrencyPriceDto
{
    public string? Currency { get; set; }
    public decimal Price { get; set; }
}

public class PlanCurrencyPricesResponse
{
    public int PlanId { get; set; }
    public List<CurrencyPriceDto> CurrencyPrices { get; set; } = new();
}

public class NextBillingDateRequest
{
    /// <summary>yyyy-MM-dd</summary>
    public string? Date { get; set; }
}
