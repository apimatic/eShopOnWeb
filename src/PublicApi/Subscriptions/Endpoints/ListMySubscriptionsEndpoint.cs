using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Endpoints;

/// <summary>
/// GET /api/my-subscriptions — lists the subscriptions of the authenticated shopper as
/// recorded in Maxio. An empty list is returned when the shopper has not subscribed yet.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user,
                UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptions,
                CancellationToken ct) =>
            {
                var userName = user.Identity?.Name;
                var shopper = userName is null ? null : await userManager.FindByNameAsync(userName);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                var items = await subscriptions.GetMySubscriptionsAsync(shopper.Id, ct);
                var response = new ListMySubscriptionsResponse { Subscriptions = items.ToList() };
                return Results.Ok(response);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult(Results.Ok());
}
