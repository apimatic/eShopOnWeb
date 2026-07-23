using System.Linq;
using System.Security.Claims;
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
/// List the authenticated caller's own subscriptions (UC1, step 7).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
                await HandleAsync(user, subscriptionService))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsResponse();

        var actor = user.ToActor();
        var subscriptions = await subscriptionService.ListSubscriptionsAsync(actor.UserName);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));

        return Results.Ok(response);
    }
}
