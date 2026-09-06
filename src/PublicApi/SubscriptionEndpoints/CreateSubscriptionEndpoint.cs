using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// Safe to replay. Sending the same request twice — a double-clicked button, a client retry after a
/// dropped response — enrolls the shopper once and answers <c>200 OK</c> the second time instead of
/// <c>201 Created</c>. By default two requests count as the same when they name the same plan and the
/// shopper already has a live subscription to it; send an <c>Idempotency-Key</c> header to define that
/// yourself, which is what you want if a shopper may legitimately hold several subscriptions to one plan.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeCommand, ISubscriptionBillingService, SubscriberResolver>
{
    /// <summary>Header a caller may send to scope the idempotency of a subscribe request.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>
    /// Generous enough for a GUID or a client-side order id, short enough that the key stays a key.
    /// </summary>
    private const int MaxIdempotencyKeyLength = 128;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request,
             HttpContext httpContext,
             ISubscriptionBillingService billing,
             SubscriberResolver subscribers) =>
            {
                var idempotencyKey = httpContext.Request.Headers[IdempotencyKeyHeader].FirstOrDefault();

                var command = new SubscribeCommand(
                    request,
                    httpContext.User,
                    idempotencyKey,
                    httpContext.RequestAborted);

                return await HandleAsync(command, billing, subscribers);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeCommand command,
        ISubscriptionBillingService billing,
        SubscriberResolver subscribers)
    {
        if (!TryReadIdempotencyKey(command.IdempotencyKey, out var idempotencyKey, out var keyProblem))
        {
            return Results.BadRequest(new { message = keyProblem });
        }

        var subscriber = await subscribers.ResolveAsync(command.Caller);

        if (subscriber is null)
        {
            // The token authenticated, but the account behind it is gone or has no email to bill against.
            return Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(
            subscriber,
            command.Body?.PlanHandle,
            idempotencyKey,
            command.CancellationToken);

        var response = new CreateSubscriptionResponse(command.Body?.CorrelationId() ?? Guid.NewGuid())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadyExisted = result.AlreadyExisted,
        };

        // No Location header: eShopOnWeb exposes subscriptions as the /api/my-subscriptions collection
        // rather than individually, and pointing at a URL that does not resolve is worse than omitting it.
        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static bool TryReadIdempotencyKey(string? raw, out string? key, out string? problem)
    {
        key = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var trimmed = raw.Trim();

        if (trimmed.Length > MaxIdempotencyKeyLength)
        {
            problem = $"{IdempotencyKeyHeader} must be at most {MaxIdempotencyKeyLength} characters.";
            return false;
        }

        // Keys are echoed into the billing system's reference field, so refuse anything that is not plain
        // printable text rather than letting control characters travel downstream.
        if (trimmed.Any(c => char.IsControl(c)))
        {
            problem = $"{IdempotencyKeyHeader} must not contain control characters.";
            return false;
        }

        key = trimmed;
        return true;
    }
}
