using System;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionCreateResponse()
    {
    }

    public bool IsSuccess { get; set; }
    public SubscriptionDto? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
