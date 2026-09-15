using System.Linq;
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
/// Lists the authenticated shopper's Maxio subscriptions.
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
            async (HttpContext httpContext, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(billing, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
        => HandleAsync(billing, httpContext: null);

    private async Task<IResult> HandleAsync(ISubscriptionBillingService billing, HttpContext? httpContext)
    {
        if (httpContext?.User is null)
        {
            return Results.Unauthorized();
        }

        var user = await ShopperIdentity.GetRequiredUserAsync(httpContext, _userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billing.ListSubscriptionsForCustomerAsync(user.Id);
        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(CreateSubscriptionEndpoint.MapSubscription).ToList()
        };
        return Results.Ok(response);
    }
}
