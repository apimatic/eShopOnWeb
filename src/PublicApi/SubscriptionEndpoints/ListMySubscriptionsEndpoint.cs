using System.Linq;
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
/// Lists the caller's Maxio subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly ShopperIdentityResolver _shopperIdentityResolver;

    public ListMySubscriptionsEndpoint(ShopperIdentityResolver shopperIdentityResolver)
    {
        _shopperIdentityResolver = shopperIdentityResolver;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        var shopper = await _shopperIdentityResolver.ResolveAsync();
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billingService.ListMySubscriptionsAsync(shopper);
        response.Subscriptions.AddRange(subscriptions.Select(ListMySubscriptionsResponse.Map));
        return Results.Ok(response);
    }
}
