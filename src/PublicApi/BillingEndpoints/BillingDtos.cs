using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Monthly price in major currency units (e.g. dollars).</summary>
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? Brand { get; set; }
}

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListPlansResponse() { }

    public List<PlanDto> Plans { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public long BalanceInCents { get; set; }
    public PlanDto? Plan { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? Currency { get; set; }
}

public class SubscriptionDetailResponse : BaseResponse
{
    public SubscriptionDetailResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionDetailResponse() { }

    public SubscriptionDto? Subscription { get; set; }
}

public class PlanResponse : BaseResponse
{
    public PlanResponse(Guid correlationId) : base(correlationId) { }
    public PlanResponse() { }

    public PlanDto? Plan { get; set; }
}

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to.</summary>
    public string? PlanHandle { get; set; }
    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }
    public string? Plan { get; set; }

    public string? ResolveHandle() =>
        FirstNonBlank(PlanHandle, Handle, Plan);

    internal static string? FirstNonBlank(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }
}

public class SwitchPlanRequest : SubscribeRequest
{
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
    public decimal Quantity { get; set; }
    public List<UsageEntryDto> Usages { get; set; } = new();
}

public class DailyUsageResponse : BaseResponse
{
    public DailyUsageResponse(Guid correlationId) : base(correlationId) { }
    public DailyUsageResponse() { }

    public int SubscriptionId { get; set; }
    public string? Date { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Monthly price in major currency units.</summary>
    public decimal? Price { get; set; }
    /// <summary>Alias of <see cref="Price"/>.</summary>
    public decimal? MonthlyPrice { get; set; }
    /// <summary>Name of one of the catalog's brands.</summary>
    public string? Brand { get; set; }
    /// <summary>Id of one of the catalog's brands.</summary>
    public int? BrandId { get; set; }
}

public class CurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class CurrencyPriceDto
{
    public int? Id { get; set; }
    public string? Currency { get; set; }
    public double? Price { get; set; }
    public string? FormattedPrice { get; set; }
}

public class CurrencyPriceResponse : BaseResponse
{
    public CurrencyPriceResponse(Guid correlationId) : base(correlationId) { }
    public CurrencyPriceResponse() { }

    public int PlanId { get; set; }
    public List<CurrencyPriceDto> CurrencyPrices { get; set; } = new();
}

public class NextBillingDateRequest : BaseRequest
{
    /// <summary>The date (YYYY-MM-DD) the subscriber is next billed.</summary>
    public string? Date { get; set; }
    /// <summary>Alias of <see cref="Date"/>.</summary>
    public string? NextBillingDate { get; set; }
}
