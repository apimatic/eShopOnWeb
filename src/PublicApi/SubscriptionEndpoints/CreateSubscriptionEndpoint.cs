using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan.
/// <para>
/// Idempotent: a repeated request — a double-clicked button, a retried call — returns the subscription the
/// shopper already holds with <c>created: false</c> and HTTP 200, instead of enrolling them twice.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        // The shopper being subscribed always comes from the token, never from the request body.
        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
        {
            return BillingResults.Unauthenticated();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                detail: $"'{nameof(request.PlanHandle)}' is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid subscription request");
        }

        try
        {
            var result = await _subscriptionBillingService.SubscribeAsync(subscriber, request.PlanHandle!, cancellationToken);

            response.Subscription = result.Subscription.ToDto();
            response.Created = result.Created;

            return result.Created
                ? Results.Created($"api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return BillingResults.Problem(ex, response.CorrelationId());
        }
    }
}
