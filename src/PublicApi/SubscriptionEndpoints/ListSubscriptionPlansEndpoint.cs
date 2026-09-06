using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ListSubscriptionPlansEndpoint
{
    public static void MapListSubscriptionPlansEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
           .Produces<SubscriptionPlanDto[]>()
           .WithName("GetSubscriptionPlans")
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        MaxioSubscriptionService subscriptionService,
        CancellationToken ct)
    {
        try
        {
            var plans = await subscriptionService.ListSubscriptionPlansAsync(ct);
            return Results.Ok(plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                PriceUSD = p.PriceUSD,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToArray());
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
