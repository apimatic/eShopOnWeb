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
/// List the subscription plans on offer
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, HttpContext context) =>
            {
                return await HandleAsync(subscriptionService, context);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, HttpContext context)
    {
        var response = new ListSubscriptionPlansResponse();

        var catalog = await subscriptionService.GetPlansAsync(context.RequestAborted);

        response.Plans.AddRange(catalog.Plans.Select(plan => plan.ToDto()));
        response.IsComplete = catalog.IsComplete;
        response.DefaultPlanHandle = catalog.DefaultPlanHandle;
        return Results.Ok(response);
    }
}
