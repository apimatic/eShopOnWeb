using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>JWT-protected Maxio subscription endpoints.</summary>
public sealed class SubscriptionEndpoints
{
    public static void MapRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/subscription-plans", GetPlansAsync)
            .Produces<SubscriptionPlanDto[]>()
            .WithTags("Subscriptions");

        app.MapPost("/api/subscriptions", SubscribeAsync)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("Subscriptions");

        app.MapGet("/api/my-subscriptions", GetMySubscriptionsAsync)
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> GetPlansAsync(HttpContext context, ISubscriptionService service, ILogger<SubscriptionEndpoints> logger, CancellationToken cancellationToken)
    {
        if (!await IsJwtAuthenticatedAsync(context)) return Results.Unauthorized();
        try
        {
            var plans = await service.GetPlansAsync(cancellationToken);
            return Results.Ok(plans);
        }
        catch (Exception exception) when (IsMaxioConfigurationError(exception))
        {
            logger.LogError(exception, "Maxio configuration is invalid.");
            return Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException exception)
        {
            logger.LogError(exception, "Maxio failed while listing subscription plans.");
            return Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> SubscribeAsync(CreateSubscriptionRequest request, HttpContext context, ISubscriptionService service, ILogger<SubscriptionEndpoints> logger, CancellationToken cancellationToken)
    {
        var userName = await GetAuthenticatedUserNameAsync(context);
        if (userName is null) return Results.Unauthorized();
        try
        {
            var result = await service.SubscribeAsync(userName, request.PlanHandle, cancellationToken);
            return result.Created
                ? Results.Created($"api/subscriptions/{result.Subscription.Id}", result)
                : Results.Ok(result);
        }
        catch (SubscriptionValidationException exception)
        {
            return Results.ValidationProblem(new[] { new KeyValuePair<string, string[]>("planHandle", new[] { exception.Message }) }.ToDictionary());
        }
        catch (Exception exception) when (IsMaxioConfigurationError(exception))
        {
            logger.LogError(exception, "Maxio configuration is invalid.");
            return Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException exception)
        {
            logger.LogError(exception, "Maxio failed while subscribing a shopper.");
            return Results.Problem("Subscription billing could not be completed.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> GetMySubscriptionsAsync(HttpContext context, ISubscriptionService service, ILogger<SubscriptionEndpoints> logger, CancellationToken cancellationToken)
    {
        var userName = await GetAuthenticatedUserNameAsync(context);
        if (userName is null) return Results.Unauthorized();
        try
        {
            var subscriptions = await service.GetMySubscriptionsAsync(userName, cancellationToken);
            return Results.Ok(new MySubscriptionsResponse { Subscriptions = new(subscriptions) });
        }
        catch (SubscriptionValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (Exception exception) when (IsMaxioConfigurationError(exception))
        {
            logger.LogError(exception, "Maxio configuration is invalid.");
            return Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException exception)
        {
            logger.LogError(exception, "Maxio failed while listing a shopper's subscriptions.");
            return Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<bool> IsJwtAuthenticatedAsync(HttpContext context) =>
        await GetAuthenticatedUserNameAsync(context) is not null;

    private static async Task<string?> GetAuthenticatedUserNameAsync(HttpContext context)
    {
        var authentication = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!authentication.Succeeded || authentication.Principal is null) return null;

        var userName = authentication.Principal.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(userName) ? null : userName;
    }

    private static bool IsMaxioConfigurationError(Exception exception) => exception is InvalidOperationException;
}
