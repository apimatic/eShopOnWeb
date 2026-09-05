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
/// Lists the calling (JWT-authenticated) user's Maxio subscriptions. Returns an empty list for
/// a user who has never subscribed - no local state is required, Maxio is the source of truth.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var buyerReference = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(buyerReference))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest(buyerReference), billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.GetSubscriptionsForBuyerAsync(request.BuyerReference);
        response.Subscriptions.AddRange(subscriptions.Select(UserSubscriptionDto.FromMaxio));

        return Results.Ok(response);
    }
}
