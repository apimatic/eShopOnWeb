using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanEndpoint : IEndpoint<IResult, object, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioBillingService service) =>
            {
                return await HandleAsync(new object(), service);
            })
            .Produces<List<SubscriptionPlanEndpointResponse>>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(object request, MaxioBillingService service)
    {
        var responses = new List<SubscriptionPlanEndpointResponse>();
        try
        {
            var pro = await service.Client.Products.ReadProductByHandle("eshop-pro");
            if (pro?.Product is not null)
            {
                responses.Add(new SubscriptionPlanEndpointResponse
                {
                    PlanHandle = pro.Product.Handle ?? "eshop-pro",
                    Name = pro.Product.Name ?? "Pro",
                    PriceInCents = (int)(pro.Product.PriceInCents ?? 0),
                    IntervalUnit = pro.Product.IntervalUnit ?? "month",
                    Interval = (int)(pro.Product.Interval ?? 1)
                });
            }

            var basic = await service.Client.Products.ReadProductByHandle("basic-plan");
            if (basic?.Product is not null)
            {
                responses.Add(new SubscriptionPlanEndpointResponse
                {
                    PlanHandle = basic.Product.Handle ?? "basic-plan",
                    Name = basic.Product.Name ?? "Basic",
                    PriceInCents = (int)(basic.Product.PriceInCents ?? 0),
                    IntervalUnit = basic.Product.IntervalUnit ?? "month",
                    Interval = (int)(basic.Product.Interval ?? 1)
                });
            }

            var comp = await service.Client.Components.FindComponent("api-call");
            if (comp?.Component is not null)
            {
                foreach (var r in responses)
                {
                    r.ComponentHandle = comp.Component.Handle ?? "api-call";
                    r.ComponentName = comp.Component.Name ?? "API Call";
                }
            }
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 500);
        }
        return Results.Ok(responses);
    }
}
