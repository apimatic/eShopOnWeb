using System.Security.Claims;
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
/// Lists the authenticated shopper's own subscriptions.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, string, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var userReference = user.Identity?.Name;
                if (string.IsNullOrEmpty(userReference))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(userReference, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string userReference, IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(await subscriptionService.GetMySubscriptionsAsync(userReference));
        return Results.Ok(response);
    }
}
