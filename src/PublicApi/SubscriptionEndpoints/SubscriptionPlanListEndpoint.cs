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
/// Lists the available subscription plans
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioBillingService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioBillingService billingService, HttpContext httpContext) =>
            {
                return await HandleAsync(billingService, httpContext);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioBillingService billingService, HttpContext httpContext)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await billingService.ListPlansAsync(httpContext.RequestAborted);
        response.SubscriptionPlans.AddRange(plans);

        return Results.Ok(response);
    }
}
