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
/// Lists Maxio subscriptions for the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing,
                   UserManager<ApplicationUser> userManager,
                   HttpContext httpContext) =>
            {
                return await HandleAsync(billing, userManager, httpContext.User);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        HandleAsync(billing, null, null);

    private static async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser>? userManager,
        ClaimsPrincipal? principal)
    {
        var user = await ResolveUserAsync(userManager, principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billing.ListSubscriptionsForUserAsync(user.Id);
        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(CreateSubscriptionEndpoint.Map).ToList()
        };

        return Results.Ok(response);
    }

    private static async Task<ApplicationUser?> ResolveUserAsync(
        UserManager<ApplicationUser>? userManager,
        ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name;
        if (userManager is null || string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }
}
