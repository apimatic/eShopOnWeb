using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// The call is idempotent. A shopper who is already on the requested plan gets 200 with their
/// existing subscription instead of a second enrollment; a genuinely new enrollment returns 201.
/// Supplying an <c>Idempotency-Key</c> (header or body) makes retries of one specific attempt return
/// that same subscription even after it has been cancelled.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest body, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                UserManager<ApplicationUser> userManager,
                [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKeyHeader,
                CancellationToken cancellationToken) =>
            {
                if (body is null || string.IsNullOrWhiteSpace(body.PlanHandle))
                {
                    // Same payload shape the exception middleware writes, so every error from this
                    // API reads the same way.
                    var error = new ErrorDetails
                    {
                        StatusCode = StatusCodes.Status400BadRequest,
                        Message = "planHandle is required. Call GET /api/subscription-plans for the available handles."
                    };
                    return Results.Content(error.ToString(), "application/json", statusCode: StatusCodes.Status400BadRequest);
                }

                var subscriber = await SubscriberIdentityResolver.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                var request = new CreateSubscriptionRequest(
                    subscriber,
                    body.PlanHandle!,
                    body.IdempotencyKey ?? idempotencyKeyHeader,
                    cancellationToken);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var result = await subscriptionService.SubscribeAsync(
            request.Subscriber, request.PlanHandle, request.IdempotencyKey, request.CancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Plan = result.Plan.ToDto(),
            AlreadySubscribed = !result.Created
        };

        return result.Created
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
