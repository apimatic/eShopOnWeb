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
/// Lists the authenticated user's subscriptions, straight from Maxio (the billing system of record).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptionService, ClaimsPrincipal caller) =>
            {
                return await HandleAsync(subscriptionService, caller);
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(401)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, ClaimsPrincipal caller)
    {
        var userId = caller.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(userId);

        var response = new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(CreateSubscriptionEndpoint.ToDto).ToList(),
        };

        return Results.Ok(response);
    }
}
