using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling shopper to a plan. Idempotent: an already-open subscription to
/// the requested plan is returned instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             HttpContext httpContext,
             UserManager<ApplicationUser> userManager,
             ISubscriptionService subscriptions,
             CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request?.PlanHandle))
                {
                    return Results.BadRequest(new { message = "A 'planHandle' is required." });
                }

                var userName = httpContext.User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(userName);
                if (user == null)
                {
                    return Results.NotFound(new { message = "The authenticated user account could not be found." });
                }

                try
                {
                    var subscription = await subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                    var response = new CreateSubscriptionResponse(request!.CorrelationId());
                    response.Subscription = subscription.ToSubscriptionDto();
                    return Results.Ok(response);
                }
                catch (SubscriptionPlanNotFoundException ex)
                {
                    return Results.NotFound(new { message = ex.Message });
                }
                catch (MaxioApiException ex)
                {
                    var statusCode = (int)ex.StatusCode >= 400 && (int)ex.StatusCode < 500 ? (int)ex.StatusCode : 502;
                    return Results.Json(new { message = ex.Message }, statusCode: statusCode);
                }
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
