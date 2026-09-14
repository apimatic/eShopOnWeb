using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions the calling shopper holds in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext,
             UserManager<ApplicationUser> userManager,
             ISubscriptionService subscriptions,
             CancellationToken cancellationToken) =>
            {
                var userName = httpContext.User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(userName);
                if (user == null)
                {
                    return Results.NotFound(new { message = "The authenticated user account could not be found." });
                }

                var all = await subscriptions.GetSubscriptionsAsync(user, cancellationToken);
                var response = new ListMySubscriptionsResponse();
                response.Subscriptions.AddRange(all.Select(s => s.ToSubscriptionDto()));
                return Results.Ok(response);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
