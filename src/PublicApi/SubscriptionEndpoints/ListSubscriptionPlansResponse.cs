using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

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

    internal static ListSubscriptionPlansResponse From(IEnumerable<SubscriptionPlan> plans)
    {
        var response = new ListSubscriptionPlansResponse();
        response.Plans.AddRange(plans.Select(MapPlan));
        return response;
    }

    private static SubscriptionPlanDto MapPlan(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequireCreditCard = plan.RequireCreditCard,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };
}
