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
/// UC1 (hero): enrolls the authenticated user in a plan. Idempotent on the user reference — a repeat
/// call while an active subscription already exists returns that subscription rather than creating a
/// second enrollment (handled in <c>SubscriptionService</c>).
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/subscribe",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                var userReference = principal.Identity?.Name;
                if (string.IsNullOrEmpty(userReference)) return Results.Unauthorized();

                var context = new SubscriptionEndpointContext(subscriptionService, userReference, principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
                return await HandleAsync(request, context);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionEndpointContext context)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await context.SubscriptionService.SubscribeAsync(
            context.UserReference,
            request.Email,
            request.FirstName,
            request.LastName,
            request.ProductHandle);

        response.Subscription = subscription;
        return Results.Ok(response);
    }
}
