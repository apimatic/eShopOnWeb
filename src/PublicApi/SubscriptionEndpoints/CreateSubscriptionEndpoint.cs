using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptions,
                UserManager<ApplicationUser> userManager, ILogger<CreateSubscriptionEndpoint> logger, CancellationToken cancellationToken) =>
            {
                var response = new CreateSubscriptionResponse(request.CorrelationId());

                // The caller identity comes from the token, never from the body.
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                    return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                    return Results.Problem("planHandle is required.", statusCode: StatusCodes.Status400BadRequest);

                var account = await userManager.FindByNameAsync(userName);
                var email = account?.Email ?? (userName.Contains('@') ? userName : string.Empty);

                try
                {
                    var result = await subscriptions.SubscribeAsync(userName, email, request.PlanHandle.Trim(), cancellationToken);
                    response.Subscription = result.Subscription;
                    response.AlreadySubscribed = result.AlreadySubscribed;
                    return result.AlreadySubscribed
                        ? Results.Ok(response)
                        : Results.Created("api/my-subscriptions", response);
                }
                catch (SubscriptionException ex)
                {
                    logger.LogWarning("Subscribe failed ({Failure}) correlation {CorrelationId}", ex.Failure, request.CorrelationId());
                    return SubscriptionProblem.ToResult(ex);
                }
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }
}
