using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions as recorded in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal claimsPrincipal, SubscriptionBillingService billingService) =>
            {
                return await HandleAsync(claimsPrincipal, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal claimsPrincipal, SubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse();

        var shopper = await billingService.ResolveShopperAsync(claimsPrincipal);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billingService.GetSubscriptionsAsync(shopper);
        response.Subscriptions = subscriptions.Select(SubscriptionMapper.ToDto).ToList();

        return Results.Ok(response);
    }
}
