using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

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
                await HandleForUserAsync(billing, user))
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        HandleForUserAsync(billing, new ClaimsPrincipal());

    private async Task<IResult> HandleForUserAsync(ISubscriptionBillingService billing, ClaimsPrincipal user)
    {
        var buyer = await ResolveBuyerAsync(_userManager, user);
        if (buyer is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billing.ListMySubscriptionsAsync(buyer.Reference);
        response.Subscriptions.AddRange(subscriptions.Select(ToDto));
        return Results.Ok(response);
    }

    internal static ShopperSubscriptionDto ToDto(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        State = subscription.State,
        Price = subscription.Price,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        Reference = subscription.Reference ?? string.Empty
    };

    internal static async Task<BillingBuyer?> ResolveBuyerAsync(
        UserManager<ApplicationUser> userManager, ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var appUser = await userManager.FindByNameAsync(userName);
        if (appUser is null)
        {
            return null;
        }

        return BillingBuyerFactory.From(appUser);
    }
}
