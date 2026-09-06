using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller to a plan.
/// </summary>
/// <remarks>
/// Idempotent by design: the caller's billing customer is created only if missing, and a shopper
/// ends up with at most one live subscription per plan no matter how many times this is called.
/// Send an <c>Idempotency-Key</c> header to tie retries to one subscribe intent explicitly.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, SubscriberService>
{
    /// <summary>Standard header callers use to mark several requests as the same attempt.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ClaimsPrincipal caller,
             SubscriberService subscribers,
             [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
             CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, caller, subscribers, idempotencyKey, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal caller, SubscriberService subscribers) =>
        HandleAsync(request, caller, subscribers, null, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal caller,
        SubscriberService subscribers,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = $"{nameof(CreateSubscriptionRequest.PlanHandle)} is required. Call api/subscription-plans to see the plans on offer."
            });
        }

        var result = await subscribers.SubscribeAsync(
            caller,
            request.PlanHandle,
            request.PaymentCollectionMethod,
            idempotencyKey,
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadySubscribed = result.AlreadySubscribed
        };

        // A repeat of a subscribe that already succeeded is not a new creation, so it answers 200.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
