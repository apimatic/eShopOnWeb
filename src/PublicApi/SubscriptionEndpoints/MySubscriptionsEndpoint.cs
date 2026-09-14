using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans the authenticated shopper is subscribed to.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public MySubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var customer = await SubscriptionEndpointHelpers.ResolveCustomerProfileAsync(httpContext, httpContext.RequestAborted);
        if (customer is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await _subscriptionService.ListSubscriptionsAsync(customer, httpContext.RequestAborted);
            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionEndpointHelpers.ToSubscriptionDto));
            return Results.Ok(response);
        }
        catch (MaxioException ex)
        {
            return SubscriptionEndpointHelpers.ToErrorResult(ex);
        }
    }
}
