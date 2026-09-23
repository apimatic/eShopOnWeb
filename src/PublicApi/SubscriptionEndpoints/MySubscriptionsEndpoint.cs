using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions, reflecting the current Maxio state.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, HttpContext http) =>
            {
                return await HandleAsync(billingService, http);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var subscriber = new SubscriberIdentity { BuyerId = buyerId, Email = buyerId };

        try
        {
            var subscriptions = await billingService.GetMySubscriptionsAsync(subscriber, http.RequestAborted);
            return Results.Ok(new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
            });
        }
        catch (SubscriptionBillingException ex)
        {
            return ex.ToResult();
        }
    }
}
