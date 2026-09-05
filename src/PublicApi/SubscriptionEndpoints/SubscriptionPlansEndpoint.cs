using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists the live subscription plans configured in Maxio.</summary>
public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var plans = await billing.ListPlansAsync(default);
        return Results.Ok(new SubscriptionPlansResponse(plans
            .Select(plan => new SubscriptionPlanResponse(plan.Handle, plan.Name, plan.PriceInCents, plan.Interval, plan.IntervalUnit))
            .ToList()));
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionBillingService billing, HttpContext context) =>
            {
                var plans = await billing.ListPlansAsync(context.RequestAborted);
                return Results.Ok(new SubscriptionPlansResponse(plans
                    .Select(plan => new SubscriptionPlanResponse(plan.Handle, plan.Name, plan.PriceInCents, plan.Interval, plan.IntervalUnit))
                    .ToList()));
            })
            .RequireAuthorization("PublicApiJwt")
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");
    }
}
