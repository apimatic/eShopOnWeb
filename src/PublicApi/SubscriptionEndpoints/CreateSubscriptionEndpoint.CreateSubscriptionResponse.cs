using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Currency { get; set; }
    public string? PlanName { get; set; }
    public string? PlanHandle { get; set; }
}
