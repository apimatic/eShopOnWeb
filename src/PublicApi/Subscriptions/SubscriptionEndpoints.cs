using System.Security.Claims;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Authenticated subscription plan discovery and enrollment endpoints.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };

        app.MapGet("api/subscription-plans", async (ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
                Results.Ok(await subscriptions.GetPlansAsync(cancellationToken)))
            .RequireAuthorization(authorize)
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." } });
                }

                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName)) return Results.Unauthorized();
                return Results.Ok(await subscriptions.SubscribeAsync(userName, request.PlanHandle, cancellationToken));
            })
            .RequireAuthorization(authorize)
            .Produces<SubscriptionResponse>()
            .ProducesValidationProblem()
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal user, ISubscriptionService subscriptions,
                CancellationToken cancellationToken) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName)) return Results.Unauthorized();
                return Results.Ok(await subscriptions.GetMySubscriptionsAsync(userName, cancellationToken));
            })
            .RequireAuthorization(authorize)
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    // Required by the endpoint discovery interface. The routed handlers above carry the
    // authenticated caller context; this method supplies the route-independent plan result.
    public async Task<IResult> HandleAsync(ISubscriptionService subscriptions) =>
        Results.Ok(await subscriptions.GetPlansAsync(CancellationToken.None));
}
