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
/// Lists the calling user's subscriptions, read from Maxio (the billing system of record)
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(httpContext, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriber = await SubscriberResolver.ResolveAsync(httpContext);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billingService.ListSubscriptionsAsync(subscriber, httpContext.RequestAborted);

        response.Subscriptions.AddRange(subscriptions.Subscriptions.Select(SubscriptionDto.From));
        response.PendingConfirmation = subscriptions.PendingConfirmation;

        return Results.Ok(response);
    }
}
