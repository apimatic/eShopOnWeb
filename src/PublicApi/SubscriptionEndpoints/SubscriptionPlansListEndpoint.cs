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
/// Lists the subscription plans available in the configured product family.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(httpContext, subscriptionBillingService);
            })
            .Produces<SubscriptionPlansListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ISubscriptionBillingService subscriptionBillingService)
    {
        var response = new SubscriptionPlansListResponse();
        var plans = await subscriptionBillingService.ListPlansAsync(httpContext.RequestAborted);

        response.Plans.AddRange(plans.Select(SubscriptionPlanDto.FromSubscriptionPlan));

        return Results.Ok(response);
    }
}
