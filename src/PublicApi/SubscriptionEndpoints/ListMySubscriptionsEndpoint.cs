using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscriptions of the authenticated user
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var subscriber = await SubscriptionCallerResolver.ResolveAsync(httpContext);
        if (subscriber == null)
        {
            return Results.Unauthorized();
        }

        var subscriptionService = httpContext.RequestServices.GetRequiredService<ISubscriptionService>();
        var response = new ListMySubscriptionsResponse();
        var subscriptions = await subscriptionService.ListSubscriptionsAsync(subscriber, httpContext.RequestAborted);
        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));

        return Results.Ok(response);
    }
}
