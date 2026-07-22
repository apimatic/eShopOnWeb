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
/// Enrol the authenticated caller in a subscription plan (UC1, the hero flow).
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                // The caller subscribes themselves; the plan handle is the only thing they choose.
                request.UserName = user.Identity?.Name;
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService) =>
        SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.PlanHandle))
            {
                return Results.BadRequest(new { error = "planHandle is required." });
            }

            var response = new SubscribeResponse(request.CorrelationId());
            var subscription = await subscriptionService.SubscribeAsync(
                request.UserName, request.PlanHandle, request.CancellationToken);

            response.Subscription = SubscriptionEndpointSupport.ToDto(subscription);

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        });
}
