using System;
using System.Net;
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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>JWT-authenticated subscription endpoints backed by Maxio Advanced Billing.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
                Results.Ok(new SubscriptionPlanResponse { Plans = await subscriptions.GetPlansAsync(cancellationToken) }))
            .Produces<SubscriptionPlanResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext context, UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var user = await GetCurrentUserAsync(context.User, userManager);
                if (user is null) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(request.PlanHandle)) return Results.BadRequest(new { message = "planHandle is required." });

                try
                {
                    var result = await subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                    return result.AlreadySubscribed
                        ? Results.Ok(result)
                        : Results.Created("/api/my-subscriptions", result);
                }
                catch (SubscriptionValidationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
                catch (SubscriptionConflictException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
                catch (MaxioApiException ex)
                {
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                        title: "Maxio billing service request failed.", detail: $"Maxio returned HTTP {(int)ex.StatusCode}.");
                }
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, UserManager<ApplicationUser> userManager, ISubscriptionService subscriptions,
                CancellationToken cancellationToken) =>
            {
                var user = await GetCurrentUserAsync(context.User, userManager);
                if (user is null) return Results.Unauthorized();

                try
                {
                    return Results.Ok(new MySubscriptionsResponse
                    {
                        Subscriptions = await subscriptions.GetMySubscriptionsAsync(user, cancellationToken)
                    });
                }
                catch (MaxioApiException ex)
                {
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                        title: "Maxio billing service request failed.", detail: $"Maxio returned HTTP {(int)ex.StatusCode}.");
                }
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(ISubscriptionService request) => throw new NotSupportedException();

    private static async Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(username) ? null : await userManager.FindByNameAsync(username);
    }
}
