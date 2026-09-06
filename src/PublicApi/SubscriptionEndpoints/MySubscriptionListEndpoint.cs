using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated shopper, newest first.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionBillingService>
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
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ISubscriptionBillingService billingService)
    {
        var subscriber = SubscriberFactory.FromPrincipal(httpContext.User);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billingService.ListSubscriptionsAsync(subscriber, httpContext.RequestAborted);

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.ToDto));

        return Results.Ok(response);
    }
}
