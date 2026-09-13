using System;

namespace Microsoft.eShopWeb.PublicApi.MaxioBillingEndpoints;

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionCreateResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
    public bool AlreadySubscribed { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long BalanceInCents { get; set; }
}
