using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists Maxio subscriptions for the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly BillingShopperResolver _shopperResolver;

    public ListMySubscriptionsEndpoint(BillingShopperResolver shopperResolver)
    {
        _shopperResolver = shopperResolver;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billingService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(billingService, user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        HandleAsync(billingService, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, ClaimsPrincipal user)
    {
        var response = new ListMySubscriptionsResponse();
        var shopper = await _shopperResolver.ResolveAsync(user, default);
        var subscriptions = await billingService.GetSubscriptionsAsync(shopper, default);
        response.Subscriptions.AddRange(subscriptions.Select(UserSubscriptionMapping.ToDto));
        return Results.Ok(response);
    }
}
