using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling user's subscriptions (plan, price, state, next billing date). Never creates a billing customer.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, SubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(subscriptionService, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new ListMySubscriptionsResponse();

        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.GetMySubscriptionsAsync(new BillingUser(userName, null), httpContext.RequestAborted);

        response.HasBillingAccount = result.HasBillingAccount;
        response.PendingSubscriptionReference = result.PendingSubscriptionReference;
        response.Subscriptions.AddRange(result.Subscriptions.Select(SubscriptionDto.From));

        return Results.Ok(response);
    }
}
