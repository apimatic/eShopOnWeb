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

/// <summary>UC1/UC4 read model: every subscription belonging to the authenticated eShopOnWeb user.</summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/mine",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                var customerReference = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(new MySubscriptionsRequest(customerReference), subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.GetSubscriptionsForCustomerAsync(request.CustomerReference);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();

        return Results.Ok(response);
    }
}
