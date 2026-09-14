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
/// Lists the current subscriptions of the authenticated shopper.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(httpContext, subscriptionBillingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ISubscriptionBillingService subscriptionBillingService)
    {
        var response = new MySubscriptionsResponse();

        var userName = ApiUserContext.GetUserName(httpContext.User);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionBillingService.ListSubscriptionsAsync(userName, httpContext.RequestAborted);

        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromSubscription));

        return Results.Ok(response);
    }
}
