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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller to a plan.
/// </summary>
/// <remarks>
/// Idempotent by design: a repeated call for a plan the caller is already subscribed to returns the
/// existing subscription with 200 OK instead of creating a second one.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeApiRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeApiRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                request.UserName = user.Identity?.Name;
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<SubscribeApiResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeApiResponse>(StatusCodes.Status200OK)
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeApiRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeApiRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            // Serialised the same way ExceptionMiddleware does, so every error from this API
            // reaches the caller in one shape.
            return Results.Content(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "planHandle is required. Call GET /api/subscription-plans for the available handles."
            }.ToString(), "application/json", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await subscriptionService.SubscribeAsync(new SubscribeRequest
        {
            UserName = request.UserName,
            PlanHandle = request.PlanHandle.Trim(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Organization = request.Organization,
            IdempotencyKey = request.IdempotencyKey
        }, cancellationToken);

        var response = new SubscribeApiResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.FromSubscription(result.Subscription),
            Plan = SubscriptionPlanDto.FromPlan(result.Plan),
            AlreadySubscribed = result.AlreadySubscribed
        };

        // A repeat subscribe is not a new resource, so only a genuine enrollment reports 201.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions#{result.Subscription.Id}", response);
    }
}
