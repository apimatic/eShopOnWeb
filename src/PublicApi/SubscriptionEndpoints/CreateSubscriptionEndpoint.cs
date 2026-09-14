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
/// Subscribes the caller to a plan. The shopper is taken from the bearer token, never from the body, and
/// the call is idempotent: repeating it leaves the shopper with a single subscription per plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, CancellationToken>
{
    /// <summary>Conventional header callers use to make a POST replay-safe.</summary>
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly ISubscriptionBillingService _subscriptionBilling;
    private readonly SubscriberFactory _subscriberFactory;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService subscriptionBilling, SubscriberFactory subscriberFactory)
    {
        _subscriptionBilling = subscriptionBilling;
        _subscriberFactory = subscriberFactory;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request, HttpContext http, CancellationToken cancellationToken) =>
            {
                request ??= new CreateSubscriptionRequest();

                // A header-supplied key is equivalent to the body field, and wins when both are present.
                if (http.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header) &&
                    !string.IsNullOrWhiteSpace(header))
                {
                    request.IdempotencyKey = header.ToString();
                }

                return await HandleAsync(request, http.User, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var subscriber = await _subscriberFactory.CreateAsync(principal);

        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await _subscriptionBilling.SubscribeAsync(
            new SubscribeRequest(subscriber, request.PlanHandle, request.IdempotencyKey),
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadySubscribed = result.AlreadySubscribed,
            CustomerCreated = result.CustomerCreated,
        };

        // An idempotent replay did not create anything, so it is not a 201.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
