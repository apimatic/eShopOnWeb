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
/// Enrol the authenticated caller in a plan (UC1, the hero flow).
/// </summary>
/// <remarks>
/// Idempotent: a caller who already holds a live subscription gets it back rather than a second
/// enrolment. The subscriber is always the bearer-token identity, never a value from the request body.
/// </remarks>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
                await HandleAsync(request, user, subscriptionService))
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ClaimsPrincipal user,
        ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var actor = user.ToActor();
        var subscription = await subscriptionService.SubscribeAsync(actor.UserName, request.PlanHandle);

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
