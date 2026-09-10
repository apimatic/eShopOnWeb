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
/// Lists the authenticated shopper's own subscriptions. The identity comes from the JWT, so a caller
/// can only ever see their own enrollments.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(user, subscriptionBillingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        var subscriptions = await subscriptionBillingService.ListSubscriptionsAsync(subscriber);
        response.Subscriptions.AddRange(subscriptions.Select(CustomerSubscriptionDto.FromDomain));

        return Results.Ok(response);
    }
}
