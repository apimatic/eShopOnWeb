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
/// Lists the subscribable plans in the configured Maxio product family.
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService maxioSubscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(maxioSubscriptionService, httpContext);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService maxioSubscriptionService, HttpContext httpContext)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await maxioSubscriptionService.GetAvailablePlansAsync(httpContext.RequestAborted);
        response.Plans.AddRange(plans.Select(PlanDto.FromServiceDto));

        return Results.Ok(response);
    }
}
