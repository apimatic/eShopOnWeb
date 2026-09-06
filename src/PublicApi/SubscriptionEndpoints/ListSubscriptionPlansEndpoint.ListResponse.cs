using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class ListSubscriptionPlansEndpoint
{
    public class ListResponse : BaseResponse
    {
        public ListResponse(Guid correlationId) : base(correlationId)
        {
        }

        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }
}
