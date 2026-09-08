using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to subscribe to (GET /api/subscription-plans).
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionService.ListSubscriptionPlansAsync(CancellationToken.None);

        response.SubscriptionPlans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            PlanHandle = p.Handle,
            Name = p.Name,
            PriceAmount = p.PriceAmount,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }));

        return Results.Ok(response);
    }
}
