using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Subscription endpoints are deliberately separate from catalog, basket, and order checkout.
/// </summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    // Route registration owns the three distinct handlers; this member satisfies the endpoint scanner's
    // generic marker and is never mapped directly.
    public Task<IResult> HandleAsync() => Task.FromResult(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(new SubscriptionPlansResponse
                    {
                        SubscriptionPlans = await subscriptions.GetPlansAsync(cancellationToken)
                    });
                }
                catch (MaxioProviderException ex)
                {
                    return ProviderProblem(ex);
                }
            })
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, SubscribeRequest request, UserManager<ApplicationUser> users, IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var user = await ResolveUserAsync(principal, users);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscription = await subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                    return Results.Ok(new SubscribeResponse { Subscription = subscription });
                }
                catch (SubscriptionEnrollmentInProgressException)
                {
                    return Results.Conflict(new { message = "A subscription request for this plan is already being processed." });
                }
                catch (MaxioProviderException ex)
                {
                    return ProviderProblem(ex);
                }
            })
            .Accepts<SubscribeRequest>("application/json")
            .Produces<SubscribeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> users, IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var user = await ResolveUserAsync(principal, users);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    return Results.Ok(new MySubscriptionsResponse
                    {
                        Subscriptions = await subscriptions.GetMySubscriptionsAsync(user, cancellationToken)
                    });
                }
                catch (MaxioProviderException ex)
                {
                    return ProviderProblem(ex);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    private static Task<ApplicationUser?> ResolveUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var username = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(username)
            ? Task.FromResult<ApplicationUser?>(null)
            : users.FindByNameAsync(username);
    }

    private static IResult ProviderProblem(MaxioProviderException exception)
    {
        var status = exception.StatusCode is >= 400 and < 500
            ? exception.StatusCode.Value
            : StatusCodes.Status502BadGateway;
        return Results.Problem(statusCode: status, title: exception.Message);
    }
}
