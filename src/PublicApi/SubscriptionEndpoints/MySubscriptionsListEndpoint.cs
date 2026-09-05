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
/// Lists the caller's own subscriptions, identified from their JWT.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioSubscriptionGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioSubscriptionGateway gateway) =>
            {
                return await HandleAsync(user, gateway);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioSubscriptionGateway gateway)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse();

        var subscriptions = await gateway.GetSubscriptionsAsync(buyerId);
        response.Subscriptions.AddRange(subscriptions.Select(subscription => new CustomerSubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.PriceAmount,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingAt,
            CreatedAt = subscription.CreatedAt
        }));

        return Results.Ok(response);
    }
}
