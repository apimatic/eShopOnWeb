using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan
/// </summary>
/// <remarks>
/// Idempotent by design: subscribing twice returns the existing subscription with 200 instead of
/// enrolling the shopper again, and the billing customer is created at most once. The subscriber
/// is always the bearer of the token - there is no way to subscribe somebody else.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriberIdentity, ISubscriptionService>
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
                HttpContext httpContext,
                SubscriberIdentityResolver identityResolver,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var subscriber = await identityResolver.ResolveAsync(httpContext.User);
                if (subscriber is null) return Results.Unauthorized();

                // The header is the conventional place for an idempotency key; the body field is
                // kept for callers that cannot set headers.
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerValue))
                {
                    request.IdempotencyKey = headerValue.ToString();
                }

                return await HandleAsync(request, subscriber, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribes the authenticated user to a plan",
                description: "Ensures a billing customer exists for the caller and enrolls them in the requested plan. " +
                             "Repeating the request returns the existing subscription with 200 rather than creating a second one."));
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request,
        SubscriberIdentity subscriber,
        ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request,
        SubscriberIdentity subscriber,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle,
            request.IdempotencyKey, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Created = result.Created,
            CustomerCreated = result.CustomerCreated
        };

        return result.Created
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
