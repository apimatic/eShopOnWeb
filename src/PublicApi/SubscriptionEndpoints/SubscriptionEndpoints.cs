using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
/// Lists Maxio plans and manages subscriptions owned by the JWT-authenticated shopper.
/// </summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    // This endpoint group maps three routes in AddRoute. MinimalApi.Endpoint requires
    // a HandleAsync member even when the routes use their own request delegates.
    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        var authorization = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
        };

        app.MapGet("api/subscription-plans", async (ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            Results.Ok(new SubscriptionPlansResponse
            {
                Plans = (await subscriptions.ListPlansAsync(cancellationToken)).ToList()
            }))
            .RequireAuthorization(authorization)
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions", async (SubscribeRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager, ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanHandle))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["planHandle"] = new[] { "The planHandle field is required." }
                });
            }

            var user = await FindUserAsync(principal, userManager);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var subscription = await subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                return Results.Ok(subscription);
            }
            catch (SubscriptionValidationException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["planHandle"] = new[] { exception.Message }
                });
            }
            catch (MaxioApiException)
            {
                return Results.Problem("The billing service could not process the subscription. Please retry.", statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization(authorization)
        .Produces<SubscriptionDto>()
        .ProducesValidationProblem()
        .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
            ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
        {
            var user = await FindUserAsync(principal, userManager);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(new MySubscriptionsResponse
                {
                    Subscriptions = (await subscriptions.ListMySubscriptionsAsync(user, cancellationToken)).ToList()
                });
            }
            catch (MaxioApiException)
            {
                return Results.Problem("The billing service could not retrieve subscriptions. Please retry.", statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization(authorization)
        .Produces<MySubscriptionsResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    private static Task<ApplicationUser?> FindUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? Task.FromResult<ApplicationUser?>(null)
            : userManager.FindByIdAsync(userId);
    }
}
