using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, object, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService svc) => await HandleAsync(new object(), svc))
            .Produces<List<SubscriptionPlanResponse>>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(object request, IMaxioSubscriptionService svc)
    {
        var plans = await svc.GetAvailablePlansAsync();
        var resp = new List<SubscriptionPlanResponse>();
        foreach (var p in plans)
        {
            resp.Add(new SubscriptionPlanResponse
            {
                Handle = p.Handle,
                Name = p.Name,
                Price = p.PriceInCents / 100m,
                Period = p.Period,
                Unit = p.Unit
            });
        }
        return Results.Ok(resp);
    }
}

public class SubscriptionPlanResponse
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string Period { get; set; }
    public string Unit { get; set; }
}
