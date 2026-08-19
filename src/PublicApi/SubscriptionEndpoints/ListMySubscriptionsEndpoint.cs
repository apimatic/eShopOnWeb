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
/// List the caller's Maxio subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billing) =>
            {
                var shopper = await ShopperIdentityResolver.ResolveAsync(userManager, user);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(billing, shopper);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        Task.FromResult(Results.Unauthorized());

    private static async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        ApplicationCore.Entities.SubscriptionBilling.ShopperIdentity shopper)
    {
        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billing.ListMySubscriptionsAsync(shopper);
        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));
        return Results.Ok(response);
    }
}
