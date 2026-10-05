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
/// Lists the authenticated shopper's subscriptions, read from Maxio
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(httpContext, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();

        var mine = await subscriptionService.GetMySubscriptionsAsync(httpContext.User.Identity!.Name!, httpContext.RequestAborted);

        response.Subscriptions.AddRange(mine.Subscriptions.Select(SubscriptionDto.From));
        response.Pending.AddRange(mine.Pending.Select(PendingSubscriptionDto.From));

        return Results.Ok(response);
    }
}
