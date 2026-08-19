using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    public static ListSubscriptionPlansResponse From(IEnumerable<SubscriptionPlan> plans, Guid correlationId)
    {
        var response = new ListSubscriptionPlansResponse(correlationId);
        response.Plans.AddRange(plans.Select(SubscriptionMappings.ToDto));
        return response;
    }
}
