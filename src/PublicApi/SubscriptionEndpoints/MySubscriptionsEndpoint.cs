using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// List the caller's own subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                await HandleAsync(new MySubscriptionsRequest(user.Identity?.Name, cancellationToken), subscriptionService))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService) =>
        SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                return Results.Unauthorized();
            }

            var response = new MySubscriptionsResponse(request.CorrelationId());
            var subscriptions = await subscriptionService.ListSubscriptionsAsync(request.UserName, request.CancellationToken);

            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionEndpointSupport.ToDto));

            return Results.Ok(response);
        });
}
