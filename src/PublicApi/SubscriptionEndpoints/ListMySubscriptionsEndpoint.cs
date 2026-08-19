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
/// Lists the caller's Maxio subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing, ClaimsPrincipal user) =>
            {
                return await HandleAsync(billing, user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        throw new System.NotSupportedException("Use HandleAsync(billing, user).");
    }

    private async Task<IResult> HandleAsync(ISubscriptionBillingService billing, ClaimsPrincipal user)
    {
        var shopper = await user.ToShopperAsync(_userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billing.ListSubscriptionsAsync(shopper);
        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
