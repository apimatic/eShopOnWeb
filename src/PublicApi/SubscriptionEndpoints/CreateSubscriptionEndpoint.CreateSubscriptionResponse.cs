using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto Subscription { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public double ProductPriceInDollars { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
}
