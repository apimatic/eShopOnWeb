using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ISubscriptionBillingService service,
                ILogger<ListSubscriptionPlansEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var plans = await service.ListPlansAsync(cancellationToken);
                    return Results.Ok(new ListSubscriptionPlansResponse { Plans = plans });
                }
                catch (Exception exception) when (SubscriptionEndpointResults.IsUpstreamFailure(exception))
                {
                    return SubscriptionEndpointResults.UpstreamFailure(exception, logger);
                }
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SubscribeRequest request,
                HttpContext httpContext,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService service,
                ILogger<CreateSubscriptionEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.ProductHandle)] = new[] { "A productHandle is required." }
                    });
                }

                var user = await SubscriptionEndpointResults.GetUserAsync(httpContext, userManager);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var outcome = await service.SubscribeAsync(user, request.ProductHandle, cancellationToken);
                    return outcome.Created
                        ? Results.Created("/api/my-subscriptions", outcome.Subscription)
                        : Results.Ok(outcome.Subscription);
                }
                catch (SubscriptionPlanNotFoundException exception)
                {
                    return Results.NotFound(new { error = exception.Message });
                }
                catch (SubscriptionRequestException exception)
                {
                    return Results.UnprocessableEntity(new { error = exception.Message });
                }
                catch (Exception exception) when (SubscriptionEndpointResults.IsUpstreamFailure(exception))
                {
                    return SubscriptionEndpointResults.UpstreamFailure(exception, logger);
                }
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces<SubscriptionDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                HttpContext httpContext,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService service,
                ILogger<ListMySubscriptionsEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                var user = await SubscriptionEndpointResults.GetUserAsync(httpContext, userManager);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscriptions = await service.ListSubscriptionsAsync(user, cancellationToken);
                    return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions });
                }
                catch (Exception exception) when (SubscriptionEndpointResults.IsUpstreamFailure(exception))
                {
                    return SubscriptionEndpointResults.UpstreamFailure(exception, logger);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}

internal static class SubscriptionEndpointResults
{
    public static async Task<ApplicationUser?> GetUserAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userName = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName) ? null : await userManager.FindByNameAsync(userName);
    }

    public static bool IsUpstreamFailure(Exception exception) =>
        exception is MaxioApiException or OptionsValidationException;

    public static IResult UpstreamFailure(Exception exception, ILogger logger)
    {
        logger.LogError(exception, "The subscription billing request failed.");
        var configurationError = exception is OptionsValidationException;
        return Results.Problem(
            statusCode: configurationError ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status502BadGateway,
            title: configurationError ? "Subscription billing is not configured." : "The billing provider request failed.");
    }
}
