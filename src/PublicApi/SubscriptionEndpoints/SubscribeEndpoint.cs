using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent)
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptions,
                UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.BadRequest(new { statusCode = 400, message = "PlanHandle is required." });
                }

                var caller = await SubscriptionCaller.ResolveAsync(principal, userManager);
                if (caller == null) return Results.Unauthorized();

                var result = await subscriptions.SubscribeAsync(caller.UserId, caller.Email, caller.DisplayName,
                    request.PlanHandle.Trim(), cancellationToken);

                var response = new SubscribeResponse(request.CorrelationId())
                {
                    Subscription = result.Subscription,
                    AlreadySubscribed = result.AlreadySubscribed
                };
                return result.AlreadySubscribed
                    ? Results.Ok(response)
                    : Results.Created("api/my-subscriptions", response);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
