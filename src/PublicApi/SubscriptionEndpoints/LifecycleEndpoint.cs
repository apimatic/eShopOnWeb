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

/// <summary>
/// UC4: one management surface for pause / resume / cancel (immediate or end-of-period) / reactivate.
/// A customer may only act on their own subscription; an Administrator may act on any.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                var userReference = principal.Identity?.Name;
                if (string.IsNullOrEmpty(userReference)) return Results.Unauthorized();

                request.SubscriptionId = subscriptionId;
                var context = new SubscriptionEndpointContext(subscriptionService, userReference, principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
                return await HandleAsync(request, context);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, SubscriptionEndpointContext context)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = await context.SubscriptionService.ChangeLifecycleStateAsync(
            request.SubscriptionId,
            context.UserReference,
            context.IsAdmin,
            request.Action,
            request.EndOfPeriod,
            request.Reason);

        response.Subscription = subscription;
        return Results.Ok(response);
    }
}
