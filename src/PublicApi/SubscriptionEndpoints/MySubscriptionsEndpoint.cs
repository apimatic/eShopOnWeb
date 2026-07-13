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

/// <summary>[Authorize] GET of the calling user's own subscriptions (mirrors the storefront's "My orders").</summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/mine",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                var userReference = principal.Identity?.Name;
                if (string.IsNullOrEmpty(userReference)) return Results.Unauthorized();

                var context = new SubscriptionEndpointContext(subscriptionService, userReference, principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
                return await HandleAsync(new MySubscriptionsRequest(), context);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, SubscriptionEndpointContext context)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());
        var subscriptions = await context.SubscriptionService.GetSubscriptionsForUserAsync(context.UserReference);
        response.Subscriptions = subscriptions.ToList();
        return Results.Ok(response);
    }
}
