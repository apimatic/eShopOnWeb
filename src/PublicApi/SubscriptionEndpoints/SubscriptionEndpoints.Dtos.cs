using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int BalanceInCents { get; set; }
    public int TotalRevenueInCents { get; set; }
    public int ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public string? ExpiresAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public string? Reference { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public ListMySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }
    public int? MaxioCustomerId { get; set; }
}
