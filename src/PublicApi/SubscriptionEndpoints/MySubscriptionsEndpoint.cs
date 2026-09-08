using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions belonging to the authenticated user
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (
                ClaimsPrincipal principal,
                UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var user = await SubscriptionEndpointHelpers.FindCurrentUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.NotFound(new { Message = "The authenticated user could not be found." });
                }

                var subscriptions = await subscriptionService.ListMySubscriptionsAsync(
                    user.Id,
                    user.Email ?? user.UserName ?? string.Empty,
                    cancellationToken);

                var response = new MySubscriptionsResponse
                {
                    Subscriptions = new System.Collections.Generic.List<SubscriptionDto>(subscriptions)
                };

                return Results.Ok(response);
            })
            .Produces<MySubscriptionsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }
}
