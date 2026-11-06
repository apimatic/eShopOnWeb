using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public decimal MonthlyPrice { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

public class SubscriptionDto
{
    public long Id { get; set; }
    /// <summary>Maxio's subscription state, e.g. active, on_hold, canceled.</summary>
    public string State { get; set; } = string.Empty;
    public long PlanId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    /// <summary>True when nothing is owed on the subscription.</summary>
    public bool Paid { get; set; }
    public decimal BalanceDue { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}

public class SwitchPlanRequest
{
    public string? PlanHandle { get; set; }
}

public class UsageRecordDto
{
    public long Id { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public class ComponentUsageDto
{
    public long ComponentId { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageRecordDto> Records { get; set; } = new();
}

public class UsageResponse
{
    public long SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CreatePlanRequest
{
    public string? Name { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class CurrencyPriceDto
{
    public long PlanId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class NextBillingDateRequest
{
    public DateOnly? Date { get; set; }
}
