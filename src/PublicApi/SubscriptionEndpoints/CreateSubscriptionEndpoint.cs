using System;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeated or concurrent calls
/// for the same user + plan resolve to a single Maxio customer and subscription.
/// Returns 201 when the enrollment is new, 200 when it replays an existing one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriptionService, ICurrentUser>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, SubscriptionService subscriptionService, ICurrentUser currentUser) =>
            {
                return await HandleAsync(request, subscriptionService, currentUser);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionService subscriptionService, ICurrentUser currentUser)
    {
        var response = new CreateSubscriptionResponse(request?.CorrelationId() ?? Guid.Empty);

        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "PlanHandle is required."
            });
        }

        var userReference = currentUser.GetUserReference();
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(
            userReference,
            request.PlanHandle.Trim(),
            request.FirstName,
            request.LastName);

        response.Subscription = MapSubscription(result.Subscription);
        response.NewlyCreated = result.Created;

        return result.Created
            ? Results.Created($"/api/subscriptions/{response.Subscription.Id}", response)
            : Results.Ok(response);
    }

    internal static SubscriptionDto MapSubscription(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt,
        ExpiresAt = subscription.ExpiresAt
    };
}
