using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: a repeated submit returns the
/// existing subscription with 200 OK instead of enrolling the shopper twice.
/// </summary>
/// <remarks>
/// Both scoped collaborators are taken as handler parameters rather than constructor
/// dependencies. MinimalApi.Endpoint resolves an endpoint once, at route-registration time, so
/// anything captured in the constructor would be shared by every request &#8212; including the
/// <c>DbContext</c> behind <see cref="ISubscriberResolver"/>.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriberResolver, ISubscriptionBillingService>
{
    /// <summary>Standard header a caller can send to make a retry safe.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ISubscriberResolver subscriberResolver,
             ISubscriptionBillingService billingService,
             HttpContext httpContext,
             CancellationToken cancellationToken) =>
            {
                if (httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerKey) &&
                    !string.IsNullOrWhiteSpace(headerKey.ToString()))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(request, subscriberResolver, billingService, cancellationToken);
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriberResolver subscriberResolver,
        ISubscriptionBillingService billingService) =>
        HandleAsync(request, subscriberResolver, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriberResolver subscriberResolver,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscriber = await subscriberResolver.ResolveCurrentAsync(request.FirstName, request.LastName);
        if (subscriber is null)
        {
            return SubscriptionResults.UnknownSubscriber();
        }

        var result = await billingService.SubscribeAsync(
            new SubscribeCommand
            {
                Subscriber = subscriber,
                PlanHandle = request.PlanHandle ?? string.Empty,
                PricePointHandle = request.PricePointHandle,
                IdempotencyKey = request.IdempotencyKey
            },
            cancellationToken);

        response.Subscription = SubscriptionMapper.ToDto(result.Subscription);
        response.AlreadySubscribed = result.Outcome == SubscribeOutcome.AlreadySubscribed;

        return result.Outcome == SubscribeOutcome.Created
            ? Results.Created($"api/my-subscriptions#{result.Subscription.Id}", response)
            : Results.Ok(response);
    }
}

/// <summary>
/// Shared failure results for the subscription endpoints, shaped like
/// <see cref="ErrorDetails"/> so they match what <see cref="Middleware.ExceptionMiddleware"/>
/// returns for everything else.
/// </summary>
internal static class SubscriptionResults
{
    public static IResult UnknownSubscriber() => Results.Json(
        new ErrorDetails
        {
            StatusCode = (int)HttpStatusCode.Unauthorized,
            Message = "The authenticated user could not be resolved to an eShopOnWeb account."
        },
        statusCode: (int)HttpStatusCode.Unauthorized);
}
