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
/// Lists the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billingService) =>
            {
                // The JWT carries the username in ClaimTypes.Name (see IdentityTokenClaimService).
                var username = user.Identity?.Name;
                var appUser = username == null ? null : await userManager.FindByNameAsync(username);
                if (appUser == null)
                {
                    return Results.Unauthorized();
                }

                var request = new ListMySubscriptionsRequest
                {
                    UserId = appUser.Id,
                    Email = appUser.Email ?? appUser.UserName ?? string.Empty
                };
                return await HandleAsync(request, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.GetSubscriptionsAsync(request.UserId, request.Email);
        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.Map));

        return Results.Ok(response);
    }
}
