using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists Maxio subscriptions belonging to the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, UserManager<ApplicationUser> users, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(billing, user, users);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
        => HandleAsync(billing, user: null, users: null);

    private static async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        ClaimsPrincipal? user,
        UserManager<ApplicationUser>? users)
    {
        var shopper = await ShopperIdentityResolver.FromAsync(user, users);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billing.ListSubscriptionsAsync(shopper);
        response.Subscriptions.AddRange(subscriptions.Select(ListMySubscriptionsResponse.FromSubscription));
        return Results.Ok(response);
    }
}
