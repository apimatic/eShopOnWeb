using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? Brand { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }

    /// <summary>true when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public long BalanceInCents { get; set; }
    public int? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? OnHoldAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPlansResponse()
    {
    }

    public List<PlanDto> Plans { get; set; } = new();
}

/// <summary>Body of requests that name a plan by handle (subscribe, switch).</summary>
public class PlanHandleRequest : BaseRequest
{
    public string? PlanHandle { get; set; }

    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }

    public string? ResolvedHandle => string.IsNullOrWhiteSpace(PlanHandle) ? Handle : PlanHandle;
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
    public int? ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public string? ComponentName { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageEntryDto> Entries { get; set; } = new();
}

public class DailyUsageResponse
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = "";
    public decimal TotalQuantity { get; set; }
    public List<ComponentUsageDto> Components { get; set; } = new();
}

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }

    /// <summary>Monthly price in the site's currency, e.g. 29.90.</summary>
    public decimal? Price { get; set; }

    /// <summary>Alias of <see cref="Price"/>.</summary>
    public decimal? MonthlyPrice { get; set; }

    /// <summary>Name of one of the catalog's brands.</summary>
    public string? Brand { get; set; }

    /// <summary>Id of one of the catalog's brands (alternative to <see cref="Brand"/>).</summary>
    public int? BrandId { get; set; }
}

public class CurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class CurrencyPriceDto
{
    public int PlanId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Price { get; set; }
}

public class NextBillingDateRequest : BaseRequest
{
    /// <summary>The date to bill on, yyyy-MM-dd.</summary>
    public string? Date { get; set; }

    /// <summary>Alias of <see cref="Date"/>.</summary>
    public string? NextBillingDate { get; set; }

    public string? ResolvedDate => string.IsNullOrWhiteSpace(Date) ? NextBillingDate : Date;
}
