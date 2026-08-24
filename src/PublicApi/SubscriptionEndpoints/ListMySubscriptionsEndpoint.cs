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
/// Lists the authenticated shopper's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(user, subscriptionService);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();

        var shopper = ShopperIdentityFactory.FromClaims(user);
        var subscriptions = await subscriptionService.ListSubscriptionsAsync(shopper.Username);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductName = s.ProductName,
            ProductHandle = s.ProductHandle,
            PriceInCents = s.PriceInCents,
            Currency = s.Currency,
            NextBillingDate = s.NextBillingDate
        }));

        return Results.Ok(response);
    }
}
