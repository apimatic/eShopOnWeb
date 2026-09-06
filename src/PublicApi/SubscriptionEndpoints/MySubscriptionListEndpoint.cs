using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated shopper's own subscriptions, read live from the billing system.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ISubscriptionBillingService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, HttpContext httpContext) =>
            {
                return await HandleAsync(billingService, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, HttpContext httpContext)
    {
        var subscriber = SubscriptionEndpointHelpers.ResolveSubscriber(httpContext.User);
        if (subscriber is null)
        {
            return SubscriptionEndpointHelpers.Unauthenticated();
        }

        var response = new ListMySubscriptionsResponse();

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, httpContext.RequestAborted);
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapping.ToDto));

            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}
