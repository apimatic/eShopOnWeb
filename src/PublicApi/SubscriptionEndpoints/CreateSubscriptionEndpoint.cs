using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling shopper to a plan. Idempotent per shopper and plan: a repeat (or concurrent) request
/// never creates a second billing customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private const int MaxNameLength = 255;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.Buyer = await BuyerIdentity.ResolveAsync(user, userManager);
                return await HandleAsync(request, subscriptionService, cancellationToken);
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

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (request.Buyer is null)
        {
            return Results.Unauthorized();
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            // Same body shape the exception middleware writes for every other error.
            return Results.Content(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = validationError }.ToString(),
                "application/json", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeCommand(request.Buyer.BuyerId, request.Buyer.Email, request.PlanHandle!.Trim(), request.FirstName, request.LastName),
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            SubscriptionReference = result.SubscriptionReference,
            Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription)
        };

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.Existing:
                response.Status = "existing";
                response.Message = "You are already subscribed to this plan.";
                return Results.Ok(response);
            default:
                response.Status = "pending";
                response.Message = "Your subscription is being confirmed with the billing system. Check GET /api/my-subscriptions shortly.";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }

    private static string? Validate(CreateSubscriptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return "planHandle is required.";
        }

        if (request.PlanHandle.Length > MaxNameLength)
        {
            return "planHandle is too long.";
        }

        if (request.FirstName?.Length > MaxNameLength || request.LastName?.Length > MaxNameLength)
        {
            return $"firstName and lastName must be at most {MaxNameLength} characters.";
        }

        return null;
    }
}
