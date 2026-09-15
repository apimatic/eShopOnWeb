using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List Maxio subscriptions for the authenticated shopper
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService billing, UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _billing.ListSubscriptionsForBuyerAsync(user.UserName!, httpContext.RequestAborted);
        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(ToDto));
        return Results.Ok(response);
    }

    private static ShopperSubscriptionDto ToDto(ShopperSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.Price,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate
        };
}
