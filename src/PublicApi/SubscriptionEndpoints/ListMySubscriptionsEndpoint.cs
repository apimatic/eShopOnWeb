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
/// Lists the Maxio subscriptions belonging to the authenticated caller. The caller's identity is
/// taken from their JWT.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var response = new ListMySubscriptionsResponse();

        var userName = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(userName);
        response.Subscriptions = subscriptions.Select(SubscriptionDto.FromDomain).ToList();

        return Results.Ok(response);
    }
}
