using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's current subscriptions (GET /api/my-subscriptions).
/// JWT-authenticated; the shopper identity comes from the bearer token.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, string, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (HttpContext httpContext, IMaxioSubscriptionService subscriptionService) =>
                {
                    string? userEmail = httpContext.User.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(userEmail))
                    {
                        return Results.Unauthorized();
                    }

                    return await HandleAsync(userEmail, subscriptionService);
                })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string userEmail, IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(userEmail);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromMaxioSubscription));

        return Results.Ok(response);
    }
}
