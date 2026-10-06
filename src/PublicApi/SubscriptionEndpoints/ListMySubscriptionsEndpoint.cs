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
/// List the calling shopper's subscriptions, read from Maxio (the billing system of record)
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ShopperIdentity, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, ClaimsPrincipal user,
                UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                var shopper = await ShopperIdentity.ResolveAsync(user, userManager);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(shopper, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ShopperIdentity shopper, ISubscriptionService subscriptionService) =>
        HandleAsync(shopper, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ShopperIdentity shopper, ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(shopper.UserName, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));
        return Results.Ok(response);
    }
}
