using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class CreateSubscriptionEndpoint
{
    public class CreateResponse : BaseResponse
    {
        public CreateResponse(Guid correlationId) : base(correlationId)
        {
        }

        public SubscriptionDto? Subscription { get; set; }
    }

    public class SubscriptionDto
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string State { get; set; } = string.Empty;
        public string? ProductHandle { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CurrentPeriodEndsAt { get; set; }
        public DateTime? NextBillingAt { get; set; }
    }
}
