using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ListSubscriptionPlansEndpoint
{
    public static void MapListSubscriptionPlans(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService subscriptionService, CancellationToken ct) =>
            {
                try
                {
                    var plans = await subscriptionService.GetSubscriptionPlansAsync(ct);
                    return Results.Ok(new { plans });
                }
                catch
                {
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("GetSubscriptionPlans")
            .AllowAnonymous()
            .Produces<object>()
            .WithTags("SubscriptionEndpoints")
            .WithSummary("List available subscription plans")
            .WithDescription("Retrieve all available subscription plans");
    }
}
