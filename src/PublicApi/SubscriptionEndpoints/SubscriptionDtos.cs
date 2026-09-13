using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
    public bool RequireCreditCard { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse() { }
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }
    public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public decimal Price { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class MySubscriptionListResponse : BaseResponse
{
    public MySubscriptionListResponse() { }
    public MySubscriptionListResponse(Guid correlationId) : base(correlationId) { }
    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionDto? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
}
