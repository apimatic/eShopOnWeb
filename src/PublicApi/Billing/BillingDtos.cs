using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    /// <summary>Price per interval in major units.</summary>
    public decimal Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListPlansResponse() { }
    public List<PlanDto> Plans { get; set; } = new();
    /// <summary>True when the plan list was cut short by the page ceiling.</summary>
    public bool Truncated { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? CustomerReference { get; set; }
    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public decimal BalanceOwed { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class UsageEntryDto
{
    public long? Id { get; set; }
    public string? Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}

public class ComponentUsageDto
{
    public int ComponentId { get; set; }
    public string? ComponentName { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<UsageEntryDto> Usages { get; set; } = new();
}

public class UsageResponse : BaseResponse
{
    public UsageResponse(Guid correlationId) : base(correlationId) { }
    public UsageResponse() { }
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = "";
    public List<ComponentUsageDto> Components { get; set; } = new();
    /// <summary>True when a page ceiling cut the usage list short.</summary>
    public bool Truncated { get; set; }
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
    public decimal Price { get; set; }
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal Price { get; set; }
}

public class NextBillingDateRequest : BaseRequest
{
    /// <summary>yyyy-MM-dd</summary>
    public string? Date { get; set; }
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Price { get; set; }
}
