using System.Collections.Generic;
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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the calling shopper to a plan
/// </summary>
/// <remarks>
/// The shopper is identified by the bearer token; a billing customer is created for them on first
/// use. The operation is idempotent: repeating it (a double-click, a retry, or a replayed
/// Idempotency-Key header) returns the existing subscription with HTTP 200 instead of enrolling twice.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeToPlanRequest? body, ClaimsPrincipal user, HttpContext httpContext,
             ISubscriberFactory subscriberFactory, ISubscriptionService subscriptionService,
             IOptions<MaxioSettings> billingSettings, CancellationToken cancellationToken) =>
            {
                var subscriber = await subscriberFactory.CreateAsync(user, cancellationToken);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                var planHandle = body?.PlanHandle;
                if (string.IsNullOrWhiteSpace(planHandle))
                {
                    planHandle = billingSettings.Value.DefaultPlanHandle;
                }

                if (string.IsNullOrWhiteSpace(planHandle))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["planHandle"] = new[] { "A plan handle is required. Call GET /api/subscription-plans to list the available handles." }
                    });
                }

                // A header keeps the key out of the domain payload for clients that prefer the
                // conventional transport-level form; the body field is accepted as well.
                var idempotencyKey = httpContext.Request.Headers[IdempotencyKeyHeader].ToString();
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    idempotencyKey = body?.IdempotencyKey;
                }

                var request = new CreateSubscriptionRequest(
                    subscriber, planHandle!, body?.PricePointHandle, idempotencyKey, cancellationToken);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var subscribeRequest = new SubscribeRequest(
            request.Subscriber, request.PlanHandle, request.PricePointHandle, request.IdempotencyKey);

        var result = await subscriptionService.SubscribeAsync(subscribeRequest, request.CancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Created = result.Created,
            Subscription = result.Subscription.ToDto()
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
