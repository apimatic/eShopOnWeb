using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the caller to a plan.
/// </summary>
/// <remarks>
/// The hero flow: ensure a billing customer exists for the authenticated shopper, enroll them,
/// and confirm plan, price, state and next billing date. Idempotent - a double-clicked button or
/// a retried request answers 200 with the subscription that already exists rather than enrolling
/// a second time.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    /// <summary>Standard header callers can use instead of the body field.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ISubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager,
             HttpContext httpContext,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberIdentityResolver.ResolveAsync(
                    httpContext.User, userManager, request.FirstName, request.LastName);

                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                request.Subscriber = subscriber;

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    request.IdempotencyKey = httpContext.Request.Headers[IdempotencyKeyHeader].FirstOrDefault();
                }

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "planHandle is required. Call GET /api/subscription-plans for the available handles."
            });
        }

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeRequest(request.Subscriber, request.PlanHandle.Trim(), request.IdempotencyKey),
            cancellationToken);

        var dto = result.Subscription.ToDto();

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = dto,
            Created = result.Created,
            Message = Confirm(result)
        };

        // 201 for a new enrollment, 200 when the call was an idempotent replay of one that
        // already existed. Both point at the same place to read it back.
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private static string Confirm(SubscribeResult result)
    {
        var subscription = result.Subscription;

        var opening = result.Created
            ? $"Subscribed to {subscription.PlanName}"
            : $"Already subscribed to {subscription.PlanName}";

        var price = $"{subscription.Price.ToString("0.00", CultureInfo.InvariantCulture)} {subscription.Currency}".Trim();

        var next = subscription.NextBillingAt is { } nextBillingAt
            ? $" Next billing date: {nextBillingAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."
            : " No further billing is scheduled.";

        return $"{opening} at {price} {subscription.Interval}. " +
               $"Status: {subscription.ProviderState}.{next}";
    }
}
