using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The authenticated buyer's subscriptions, read from the billing provider (system of record)
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(context, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext context, SubscriptionService subscriptionService)
    {
        var buyerId = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var mine = await subscriptionService.GetMySubscriptionsAsync(buyerId, context.RequestAborted);

        var response = new ListMySubscriptionsResponse { HasUnconfirmedEnrollment = mine.HasUnconfirmedEnrollment };
        response.Subscriptions.AddRange(mine.Subscriptions.Select(SubscriptionDto.From));
        return Results.Ok(response);
    }
}
