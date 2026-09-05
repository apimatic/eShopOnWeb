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
/// Lists the authenticated buyer's own Maxio subscriptions. Returns an empty list if the
/// buyer has never subscribed (no Maxio customer exists for them yet).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MySubscriptionsListRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var request = new MySubscriptionsListRequest { BuyerId = user.Identity!.Name! };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<MySubscriptionsListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsListRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsListResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.GetSubscriptionsForBuyerAsync(request.BuyerId);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapping.ToDto));

        return Results.Ok(response);
    }
}
