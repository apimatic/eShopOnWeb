using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class ListUserSubscriptionsEndpoint
{
    public class ListResponse : BaseResponse
    {
        public ListResponse(Guid correlationId) : base(correlationId)
        {
        }

        public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
    }

    public class UserSubscriptionDto
    {
        public int Id { get; set; }
        public string State { get; set; } = string.Empty;
        public string? ProductHandle { get; set; }
        public string? ProductName { get; set; }
        public decimal? Price { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CurrentPeriodEndsAt { get; set; }
        public DateTime? NextBillingAt { get; set; }
    }
}
