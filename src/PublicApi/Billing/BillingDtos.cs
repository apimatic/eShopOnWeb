using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    /// <summary>The Maxio product id (used by the staff routes).</summary>
    public int Id { get; set; }
    /// <summary>The plan's handle (used by subscribe and switch).</summary>
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Monthly price in the site's primary currency.</summary>
    public decimal Price { get; set; }
    public string Interval { get; set; } = string.Empty;
    public int? BrandId { get; set; }
    public string? Brand { get; set; }
}

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
    /// <summary>True when the plan list was cut short and may be incomplete.</summary>
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
    public long BalanceInCents { get; set; }
    public string? Currency { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
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
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<UsageRecordDto> Records { get; set; } = new();
}

public class UsageResponse
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<ComponentUsageDto> Components { get; set; } = new();
    /// <summary>True when the usage list was cut short and may be incomplete.</summary>
    public bool Truncated { get; set; }
}

public class CreatePlanRequest
{
    public string? Name { get; set; }
    /// <summary>Monthly price in the site's primary currency, in major units (e.g. 9.99).</summary>
    public decimal Price { get; set; }
    /// <summary>The id of one of the shop's catalog brands.</summary>
    public int BrandId { get; set; }
}

public class CurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal Price { get; set; }
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? FormattedPrice { get; set; }
}

public class NextBillingDateRequest
{
    /// <summary>YYYY-MM-DD</summary>
    public string? Date { get; set; }
}
