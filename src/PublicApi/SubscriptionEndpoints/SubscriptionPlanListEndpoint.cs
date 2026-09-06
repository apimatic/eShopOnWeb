using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a shopper can sign up for.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, HttpContext, SubscriptionsApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, SubscriptionsApiService subscriptions) =>
            {
                return await HandleAsync(httpContext, subscriptions);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, SubscriptionsApiService subscriptions)
    {
        var response = new ListSubscriptionPlansResponse();

        response.SubscriptionPlans.AddRange(
            await subscriptions.GetPlansAsync(httpContext.RequestAborted));

        return Results.Ok(response);
    }
}
