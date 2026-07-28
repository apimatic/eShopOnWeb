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
/// Lists the authenticated shopper's subscriptions (empty when they have never subscribed).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(user.Identity?.Name), billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.SubscriberReference))
            return Results.Unauthorized();

        var subscriptions = await billingService.GetSubscriptionsAsync(request.SubscriberReference!);

        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMappings.ToDto));

        return Results.Ok(response);
    }
}
