using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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
/// Subscribes the caller to a plan. The shopper comes from the bearer token, never from the body.
/// The operation is idempotent: it creates a billing customer for the shopper only once, and a
/// repeated request returns the subscription that already exists instead of enrolling twice.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, Subscriber, ISubscriptionService>
{
    /// <summary>Header clients may use instead of the body field, matching common API convention.</summary>
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             HttpContext httpContext,
             ISubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager,
             CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }

                var subscriber = await SubscriberResolver.ResolveAsync(
                    httpContext.User, userManager, request.FirstName, request.LastName);

                return subscriber is null
                    ? Results.Unauthorized()
                    : await HandleAsync(request, subscriber, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, Subscriber subscriber, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        Subscriber subscriber,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[]
                {
                    "A plan handle is required. Call GET /api/subscription-plans for the available handles."
                }
            });
        }

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeRequest(subscriber, request.PlanHandle.Trim(), request.IdempotencyKey),
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadySubscribed = result.AlreadySubscribed
        };

        // A repeat of a request that already succeeded created nothing, so it answers 200, not 201.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created(
                $"/api/my-subscriptions#{result.Subscription.Id.ToString(CultureInfo.InvariantCulture)}",
                response);
    }
}
