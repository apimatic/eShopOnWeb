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
/// Lists the subscription plans available for signup.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(), subscriptionService);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId());

        var plans = await subscriptionService.GetAvailablePlansAsync();
        response.SubscriptionPlans.AddRange(plans.Select(SubscriptionMapping.ToDto));

        return Results.Ok(response);
    }
}
