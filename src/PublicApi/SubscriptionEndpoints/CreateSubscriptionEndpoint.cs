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
/// Subscribes the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// Repeating the call for a plan the shopper already holds returns 200 with the existing
/// subscription rather than enrolling them twice, so a double-clicked Subscribe button is harmless.
/// A fresh enrollment answers 201.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    /// <summary>Standard header callers may use instead of the body field.</summary>
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
                ClaimsPrincipal user,
                HttpContext httpContext,
                ISubscriberProfileResolver profileResolver,
                ISubscriptionService subscriptionService) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerValue))
                {
                    request.IdempotencyKey = headerValue.ToString();
                }

                // Identity always comes from the token, never from the body.
                request.Subscriber = await profileResolver.ResolveAsync(user, request.Customer);
                request.CancellationToken = httpContext.RequestAborted;

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = $"'{nameof(request.PlanHandle)}' is required." });
        }

        if (request.Subscriber is null)
        {
            // The token was accepted but no eShopOnWeb user stands behind it any more.
            return Results.Unauthorized();
        }

        var command = new SubscribeCommand(
            request.Subscriber,
            request.PlanHandle.Trim(),
            request.IdempotencyKey);

        var result = await subscriptionService.SubscribeAsync(command, request.CancellationToken);

        response.Created = result.Created;
        response.CustomerCreated = result.CustomerCreated;
        response.CustomerReference = result.CustomerReference;
        response.Subscription = result.Subscription.ToDto();

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
