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

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing,
                UserManager<ApplicationUser> userManager,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billing, userManager, user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        throw new System.NotSupportedException("Use the routed handler.");

    private async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var shopper = await ShopperIdentityFactory.FromUserAsync(userManager, user);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billing.ListMySubscriptionsAsync(shopper, cancellationToken);
        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(CreateShopperSubscriptionEndpoint.Map).ToList()
        };

        return Results.Ok(response);
    }
}
