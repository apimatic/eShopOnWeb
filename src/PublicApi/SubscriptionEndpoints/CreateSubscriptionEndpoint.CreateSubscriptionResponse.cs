using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>True if the shopper already had a live subscription to this plan and no new one was created.</summary>
    public bool AlreadyEnrolled { get; set; }
}
