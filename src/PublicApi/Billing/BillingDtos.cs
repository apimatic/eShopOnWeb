using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Brand { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public string? Interval { get; set; }
}

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId) { }
    public List<PlanDto> Plans { get; set; } = new();
}

public class PlanResponse : BaseResponse
{
    public PlanResponse(Guid correlationId) : base(correlationId) { }
    public PlanDto Plan { get; set; } = new();
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = "";
    public long PlanId { get; set; }
    public string PlanHandle { get; set; } = "";
    public string PlanName { get; set; } = "";
    /// <summary>true when nothing is owed on the subscription.</summary>
    public bool Paid { get; set; }
    public long BalanceInCents { get; set; }
    public decimal AmountDue { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class SubscriptionResponse : BaseResponse
{
    public SubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionDto Subscription { get; set; } = new();
}

public class UsageComponentDto
{
    public long ComponentId { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageRecordDto> Records { get; set; } = new();
}

public class UsageRecordDto
{
    public long Id { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public class UsageResponse : BaseResponse
{
    public UsageResponse(Guid correlationId) : base(correlationId) { }
    public long SubscriptionId { get; set; }
    public string Date { get; set; } = "";
    public decimal TotalQuantity { get; set; }
    public List<UsageComponentDto> Components { get; set; } = new();
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
    public decimal MonthlyPrice { get; set; }
    public int CatalogBrandId { get; set; }
}

public class CreateCurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal Price { get; set; }
}

public class CurrencyPriceResponse : BaseResponse
{
    public CurrencyPriceResponse(Guid correlationId) : base(correlationId) { }
    public long PlanId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Price { get; set; }
}

public class SetNextBillingDateRequest : BaseRequest
{
    /// <summary>yyyy-MM-dd</summary>
    public DateOnly? Date { get; set; }
}
