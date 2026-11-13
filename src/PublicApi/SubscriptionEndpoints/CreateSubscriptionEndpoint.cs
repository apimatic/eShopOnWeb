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
/// Subscribes the authenticated shopper to a plan. Idempotent per (user, plan): repeating the request
/// (double-click, client retry) never creates a second billing customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private const int MaxFieldLength = 255;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user.Identity?.Name, billing, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDetails>(StatusCodes.Status409Conflict)
            .Produces<ErrorDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorDetails>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
        Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, string? userName, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            // Same body shape the ExceptionMiddleware writes for every other error.
            return Results.Content(
                new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = validationError }.ToString(),
                "application/json",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await billing.SubscribeAsync(
            new SubscribeCommand(userName, request.PlanHandle!.Trim(), request.FirstName, request.LastName),
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            PlanHandle = result.PlanHandle,
            SubscriptionReference = result.SubscriptionReference,
            Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription)
        };

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Status = "already_subscribed";
                return Results.Ok(response);
            default:
                response.Status = "pending_confirmation";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }

    private static string? Validate(CreateSubscriptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return "planHandle is required. Use a handle from GET /api/subscription-plans.";
        }

        if (request.PlanHandle.Length > MaxFieldLength
            || request.FirstName?.Length > MaxFieldLength
            || request.LastName?.Length > MaxFieldLength)
        {
            return $"Fields must be at most {MaxFieldLength} characters.";
        }

        return null;
    }
}
