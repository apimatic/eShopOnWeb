using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequireCreditCard = plan.RequireCreditCard
    };
}
