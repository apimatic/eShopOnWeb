using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class SubscriptionPlansEndpoint
{
    public class ListSubscriptionPlansResponse : BaseResponse
    {
        public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }
}
