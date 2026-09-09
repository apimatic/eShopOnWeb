using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated user. Requires a valid JWT; the user identity
/// comes from the token. Returns an empty list if the user has never subscribed.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
                await HandleAsync(user, billingService))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Problem(
                detail: "The access token does not identify a user.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var billingUser = BillingUser.FromUserName(userName);
        var subscriptions = await billingService.GetSubscriptionsForUserAsync(billingUser);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => s.ToDto()).ToList(),
        };

        return Results.Ok(response);
    }
}
