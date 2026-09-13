using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    public class Response : BaseResponse
    {
        public Response(Guid correlationId) : base(correlationId)
        {
        }

        public Response()
        {
        }

        public IReadOnlyList<ApplicationCore.Interfaces.SubscriptionPlanDto> Plans { get; set; } = Array.Empty<ApplicationCore.Interfaces.SubscriptionPlanDto>();
    }
}
