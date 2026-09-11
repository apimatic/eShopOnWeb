using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioBillingService svc, HttpContext ctx) =>
            {
                var user = ctx.User.Identity?.Name ?? "unknown";
                var subs = await svc.GetSubscriptionsForCustomerAsync(user);
                return Results.Ok(subs.Select(s => new { s.Id, s.State, s.PlanName, s.Price, s.NextBillingDate, s.ActivatedAt }));
            })
            .Produces<object>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService svc)
    {
        return Results.Ok(new object());
    }
}
