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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists Maxio subscriptions owned by the authenticated shopper.</summary>
public sealed class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal principal, IMaxioSubscriptionService subscriptions, UserManager<ApplicationUser> users,
                CancellationToken cancellationToken) =>
            {
                var user = await CreateSubscriptionEndpoint.FindUserAsync(principal, users);
                if (user is null) return Results.Unauthorized();
                return Results.Ok(await subscriptions.GetMySubscriptionsAsync(user, cancellationToken));
            })
            .Produces<SubscriptionSummary[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }
}
