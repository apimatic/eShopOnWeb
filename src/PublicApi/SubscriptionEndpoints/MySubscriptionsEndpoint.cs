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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the caller's Maxio subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var shopper = await ShopperIdentityFactory.FromAsync(user, userManager);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(billing, shopper, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        throw new System.NotSupportedException("Shopper identity is required.");
    }

    private async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        ApplicationCore.Entities.SubscriptionAggregate.ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billing.ListShopperSubscriptionsAsync(shopper, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDtoMapper.ToDto));
        return Results.Ok(response);
    }
}
