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
/// Lists the current user's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, HttpContext, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, MaxioBillingService billingService) =>
            {
                return await HandleAsync(httpContext, billingService);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, MaxioBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billingService.ListMySubscriptionsAsync(httpContext.User, httpContext.RequestAborted);
        response.Subscriptions.AddRange(subscriptions);

        return Results.Ok(response);
    }
}
