using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maxio Advanced Billing subscription endpoints. These are additive to the catalogue,
/// basket and order endpoints.
/// </summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    // This endpoint groups three route handlers; routing invokes AddRoute rather than this
    // interface member, which exists solely to satisfy the MinimalApi.Endpoint contract.
    public Task<IResult> HandleAsync() => throw new NotSupportedException();

    public void AddRoute(IEndpointRouteBuilder app)
    {
        var plans = app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingClient billing, CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await billing.ListPlansAsync(cancellationToken);
                    return Results.Ok(new ListSubscriptionPlansResponse { Plans = new(result) });
                }
                catch (MaxioConfigurationException exception)
                {
                    return Unavailable(exception);
                }
                catch (MaxioApiException)
                {
                    return UpstreamFailure();
                }
            });
        plans.Produces<ListSubscriptionPlansResponse>().ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status502BadGateway).WithTags("Subscriptions");

        var subscribe = app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext context, UserManager<ApplicationUser> userManager,
                IMaxioBillingClient billing, CancellationToken cancellationToken) =>
            {
                var user = await GetCurrentUserAsync(context.User, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var response = await billing.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                    return response.AlreadySubscribed ? Results.Ok(response) : Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new { message = exception.Message });
                }
                catch (SubscriptionEnrollmentInProgressException exception)
                {
                    return Results.Conflict(new { message = exception.Message });
                }
                catch (MaxioConfigurationException exception)
                {
                    return Unavailable(exception);
                }
                catch (MaxioApiException)
                {
                    return UpstreamFailure();
                }
            });
        subscribe.Accepts<CreateSubscriptionRequest>("application/json").Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).WithTags("Subscriptions");

        var mine = app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, UserManager<ApplicationUser> userManager, IMaxioBillingClient billing,
                CancellationToken cancellationToken) =>
            {
                var user = await GetCurrentUserAsync(context.User, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscriptions = await billing.ListMySubscriptionsAsync(user, cancellationToken);
                    return Results.Ok(new MySubscriptionsResponse { Subscriptions = new(subscriptions) });
                }
                catch (MaxioConfigurationException exception)
                {
                    return Unavailable(exception);
                }
                catch (MaxioApiException)
                {
                    return UpstreamFailure();
                }
            });
        mine.Produces<MySubscriptionsResponse>().Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("Subscriptions");
    }

    private static async Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return await userManager.FindByIdAsync(userId);
        }

        // Tokens issued before the stable user-id claim was added remain usable until expiry.
        return !string.IsNullOrWhiteSpace(principal.Identity?.Name)
            ? await userManager.FindByNameAsync(principal.Identity.Name)
            : null;
    }

    private static IResult Unavailable(Exception exception) => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Subscription billing is not configured",
        detail: exception.Message);

    private static IResult UpstreamFailure() => Results.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "Subscription billing is temporarily unavailable");
}
