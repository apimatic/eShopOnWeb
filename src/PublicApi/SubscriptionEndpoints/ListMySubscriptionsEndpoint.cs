using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
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
/// Lists the authenticated shopper's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal claimsPrincipal,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService) =>
            {
                var username = claimsPrincipal.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(username);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var request = new ListMySubscriptionsRequest
                {
                    Shopper = new ShopperInfo(user.Id, user.Email ?? username, username)
                };
                return await HandleAsync(request, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = (await billingService.GetSubscriptionsAsync(request.Shopper!)).ToList()
        };
        return Results.Ok(response);
    }
}
