using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService svc) =>
            {
                var plans = await svc.GetPlansAsync();
                return Results.Ok(plans.Select(p => new { p.Handle, p.Name, p.Price, p.Interval, p.IntervalUnit, p.ProductId }));
            })
            .Produces<object>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService svc)
    {
        var plans = await svc.GetPlansAsync();
        return Results.Ok(plans.Select(p => new { p.Handle, p.Name, p.Price, p.Interval, p.IntervalUnit, p.ProductId }));
    }
}
