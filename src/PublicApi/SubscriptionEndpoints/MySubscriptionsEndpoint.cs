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
/// Lists the subscriptions of the authenticated shopper, read from the billing
/// system of record. Shoppers who never subscribed get an empty list.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(subscriptionService, user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, ClaimsPrincipal user)
    {
        var response = new ListMySubscriptionsResponse();

        var shopperEmail = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(shopperEmail))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(shopperEmail);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionApiMapper.ToDto));

        return Results.Ok(response);
    }
}
