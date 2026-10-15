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
/// List the authenticated shopper's subscriptions, read from the billing system of record
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, HttpContext context) =>
            {
                return await HandleAsync(subscriptionService, context);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, HttpContext context)
    {
        var userName = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var result = await subscriptionService.GetSubscriptionsAsync(userName, context.RequestAborted);

        response.Subscriptions.AddRange(result.Subscriptions.Select(subscription => subscription.ToDto()));
        response.PendingPlanHandle = result.PendingPlanHandle;
        return Results.Ok(response);
    }
}
