using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan.
/// <para>
/// The operation is idempotent: a billing customer is created for the shopper only if one does not
/// already exist, and repeating the call while a live subscription to the same plan exists returns
/// that subscription (200) instead of creating a second one (201).
/// </para>
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, HttpContext httpContext,
                ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header)
                    && !string.IsNullOrWhiteSpace(header))
                {
                    request.IdempotencyKey = header.ToString();
                }

                return await HandleAsync(request, user, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, user: null, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal? user,
        ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var subscriber = SubscriberIdentityFactory.From(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[]
                {
                    "A plan handle is required. Call GET /api/subscription-plans to see the available handles."
                }
            });
        }

        var result = await billingService.SubscribeAsync(
            new SubscribeRequest(subscriber, request.PlanHandle!, request.IdempotencyKey), cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Plan = result.Plan.ToDto(),
            AlreadySubscribed = result.AlreadySubscribed
        };

        response.Message = BuildMessage(response, result.AlreadySubscribed);

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created(
                $"api/my-subscriptions#{result.Subscription.Id.ToString(CultureInfo.InvariantCulture)}", response);
    }

    private static string BuildMessage(CreateSubscriptionResponse response, bool alreadySubscribed)
    {
        var planName = response.Plan?.Name ?? response.Subscription?.PlanHandle ?? "the plan";
        var price = response.Subscription?.DisplayPrice ?? response.Plan?.DisplayPrice ?? string.Empty;
        var state = response.Subscription?.State ?? "unknown";

        var opening = alreadySubscribed
            ? $"You are already subscribed to {planName}"
            : $"You are now subscribed to {planName}";

        var next = response.Subscription?.NextBillingAt is { } nextBillingAt
            ? $"; next billing date {nextBillingAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}"
            : string.Empty;

        return string.IsNullOrEmpty(price)
            ? $"{opening} (state {state}){next}."
            : $"{opening} at {price} (state {state}){next}.";
    }
}
