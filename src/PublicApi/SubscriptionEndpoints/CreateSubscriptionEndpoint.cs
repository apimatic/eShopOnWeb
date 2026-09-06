using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using BlazorShared.Models;
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
/// Subscribes the signed-in shopper to a plan, creating their billing customer on first use.
/// <para>
/// The call is safe to repeat. Without an idempotency key, a shopper who already holds a live
/// subscription to the plan gets that subscription back (HTTP 200) instead of a second one; with an
/// <c>Idempotency-Key</c>, a replay returns the originally created subscription. Only a call that
/// actually enrols the shopper answers 201.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionService>
{
    /// <summary>Conventional header for caller-supplied idempotency keys.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
           .Produces<CreateSubscriptionResponse>((int)HttpStatusCode.Created)
           .Produces<CreateSubscriptionResponse>((int)HttpStatusCode.OK)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Problem(HttpStatusCode.BadRequest, "'planHandle' is required. Call GET /api/subscription-plans for the available handles.");
        }

        if (!SubscriberIdentityFactory.TryCreate(httpContext.User, out var subscriber, out var identityError))
        {
            return Problem(HttpStatusCode.BadRequest, identityError);
        }

        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) &&
            httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerValue))
        {
            idempotencyKey = headerValue.ToString();
        }

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeRequest(subscriber, request.PlanHandle, idempotencyKey),
            httpContext.RequestAborted);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.Created = result.Created;
        response.Outcome = Describe(result.Outcome);

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }

    /// <summary>
    /// Writes the same error body shape as <see cref="Middleware.ExceptionMiddleware"/>, so callers
    /// see one error contract whether a request was rejected here or blew up downstream.
    /// </summary>
    private static IResult Problem(HttpStatusCode statusCode, string? message) =>
        Results.Content(
            new ErrorDetails { StatusCode = (int)statusCode, Message = message }.ToString(),
            "application/json",
            statusCode: (int)statusCode);

    private static string Describe(SubscribeOutcome outcome) => outcome switch
    {
        SubscribeOutcome.Created => "created",
        SubscribeOutcome.AlreadySubscribed => "already_subscribed",
        SubscribeOutcome.IdempotentReplay => "idempotent_replay",
        _ => outcome.ToString().ToLowerInvariant()
    };
}
