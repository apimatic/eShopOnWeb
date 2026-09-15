using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Lists plans from the configured Maxio product family.</summary>
public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    // MinimalApi.Endpoint discovers routes through AddRoute. Dependencies are intentionally
    // supplied by the request delegate so scoped services remain request-scoped.
    public Task<IResult> HandleAsync() => throw new NotSupportedException("Use the mapped route.");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionEnrollmentService subscriptions, CancellationToken cancellationToken) =>
            {
                var plans = await subscriptions.GetPlansAsync(cancellationToken);
                return Results.Ok(new SubscriptionPlanListResponse { Plans = plans });
            })
            .Produces<SubscriptionPlanListResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }
}

/// <summary>Enrolls the authenticated shopper in an available Maxio plan.</summary>
public sealed class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    // See SubscriptionPlanListEndpoint.HandleAsync.
    public Task<IResult> HandleAsync(CreateSubscriptionRequest request) => throw new NotSupportedException("Use the mapped route.");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext context, UserManager<ApplicationUser> userManager,
                ISubscriptionEnrollmentService subscriptions, ILogger<SubscriptionCreateEndpoint> logger, CancellationToken cancellationToken) =>
            {
                var user = await GetUserAsync(context.User, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscription = await subscriptions.EnrollAsync(user, request.PlanHandle, cancellationToken);
                    return Results.Ok(subscription);
                }
                catch (SubscriptionValidationException exception)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = new[] { exception.Message } });
                }
                catch (MaxioApiException exception)
                {
                    logger.LogError(exception, "Maxio rejected subscription enrollment for the authenticated user.");
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Maxio billing request failed.");
                }
            })
            .Produces<SubscriptionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    private static async Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName) ? null : await userManager.FindByNameAsync(userName);
    }
}

/// <summary>Lists the authenticated shopper's current Maxio subscriptions.</summary>
public sealed class MySubscriptionListEndpoint : IEndpoint<IResult>
{
    // See SubscriptionPlanListEndpoint.HandleAsync.
    public Task<IResult> HandleAsync() => throw new NotSupportedException("Use the mapped route.");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, UserManager<ApplicationUser> userManager, ISubscriptionEnrollmentService subscriptions,
                CancellationToken cancellationToken) =>
            {
                var userName = context.User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(userName);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var result = await subscriptions.GetMySubscriptionsAsync(user, cancellationToken);
                    return Results.Ok(new MySubscriptionsResponse { Subscriptions = result });
                }
                catch (MaxioApiException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Maxio billing request failed.");
                }
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }
}
