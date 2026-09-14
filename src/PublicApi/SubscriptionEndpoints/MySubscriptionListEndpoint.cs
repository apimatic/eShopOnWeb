using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions, read live from the billing provider.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, HttpContext, SubscriptionsApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, SubscriptionsApiService subscriptions) =>
            {
                return await HandleAsync(httpContext, subscriptions);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, SubscriptionsApiService subscriptions)
    {
        var response = new ListMySubscriptionsResponse();

        response.Subscriptions.AddRange(
            await subscriptions.GetMySubscriptionsAsync(httpContext.User, httpContext.RequestAborted));

        return Results.Ok(response);
    }
}
