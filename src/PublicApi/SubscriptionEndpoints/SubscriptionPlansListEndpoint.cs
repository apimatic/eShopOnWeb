using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for the site's Maxio product family.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                return await HandleAsync(maxioSubscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService maxioSubscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await maxioSubscriptionService.GetAvailablePlansAsync();

        response.SubscriptionPlans = plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            IntervalCount = p.IntervalCount,
            IntervalUnit = p.IntervalUnit
        }).ToList();

        return Results.Ok(response);
    }
}
