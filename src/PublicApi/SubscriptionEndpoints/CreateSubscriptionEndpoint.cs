using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the caller to a plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    /// <summary>Conventional header for an idempotency key, accepted as an alternative to the body field.</summary>
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request,
                   HttpContext httpContext,
                   ISubscriptionBillingService billingService,
                   CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerValue))
                {
                    request.IdempotencyKey = headerValue.ToString();
                }

                return await HandleAsync(request, httpContext.User, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                "Subscribes the authenticated shopper to a plan.",
                "Ensures the shopper has a billing customer, then enrols them. Idempotent: repeating the " +
                "call returns the existing subscription with 200 instead of creating a second one."));
    }

    public Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService) =>
        HandleAsync(request, user, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = "planHandle is required. Call GET /api/subscription-plans to see the available handles."
            });
        }

        var identity = BillingIdentityResolver.FromPrincipal(user);

        if (identity is null)
        {
            return Results.Json(
                new ErrorDetails
                {
                    StatusCode = (int)HttpStatusCode.Unauthorized,
                    Message = "The bearer token does not identify a user."
                },
                statusCode: (int)HttpStatusCode.Unauthorized);
        }

        var command = new SubscribeCommand(identity, request.PlanHandle!, request.IdempotencyKey);
        var result = await billingService.SubscribeAsync(command, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Outcome = Describe(result.Outcome),
            Created = result.Created
        };

        // A repeat of a call that already succeeded is not a new resource, so it answers 200.
        return result.Created
            ? Results.Created(
                $"api/my-subscriptions#{result.Subscription.Id.ToString(CultureInfo.InvariantCulture)}",
                response)
            : Results.Ok(response);
    }

    private static string Describe(SubscribeOutcome outcome) => outcome switch
    {
        SubscribeOutcome.Created => "created",
        SubscribeOutcome.AlreadySubscribed => "alreadySubscribed",
        SubscribeOutcome.IdempotentReplay => "idempotentReplay",
        _ => outcome.ToString()
    };
}
