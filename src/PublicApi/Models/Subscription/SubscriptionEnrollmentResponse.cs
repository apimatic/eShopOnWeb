using System;

namespace Microsoft.eShopWeb.PublicApi.Models.Subscription;

public class SubscriptionEnrollmentResponse : BaseResponse
{
    public SubscriptionEnrollmentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionEnrollmentResponse()
    {
    }

    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
