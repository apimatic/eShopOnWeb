using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Auth;
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
            async (HttpContext httpContext, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(httpContext, userManager, billing);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        throw new System.NotSupportedException("Subscriptions are handled through the routed overload.");
    }

    public async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billing)
    {
        var shopper = await ShopperResolver.FromHttpContextAsync(httpContext, userManager);
        var subscriptions = await billing.ListShopperSubscriptionsAsync(shopper);
        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(Map));
        return Results.Ok(response);
    }

    private static ShopperSubscriptionDto Map(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate
    };
}
