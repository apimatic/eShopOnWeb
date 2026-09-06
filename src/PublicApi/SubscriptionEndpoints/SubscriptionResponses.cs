using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansListResponse : BaseResponse
{
    public GetSubscriptionPlansListResponse()
    {
        Plans = new();
    }

    public List<SubscriptionPlanDto> Plans { get; set; }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = null!;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public decimal PriceMonthly { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
}

public class ErrorResponse : BaseResponse
{
    public string Message { get; set; } = null!;
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse()
    {
        Subscriptions = new();
    }

    public List<SubscriptionDetailDto> Subscriptions { get; set; }
}

public class SubscriptionDetailDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string State { get; set; } = null!;
    public decimal PriceMonthly { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
}
