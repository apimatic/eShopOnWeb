using System;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Middleware;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in a subscription plan. Idempotent: a
/// repeated request for the same user and plan returns the existing
/// subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, System.Security.Claims.ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, subscriptionService, user);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    Task<IResult> IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>.HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        throw new NotSupportedException("Use HandleAsync(CreateSubscriptionRequest, ISubscriptionService, ClaimsPrincipal) instead.");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        System.Security.Claims.ClaimsPrincipal user)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "planHandle is required."
            });
        }

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(username, request.PlanHandle);

            response.Subscription = new SubscriptionSummaryDto
            {
                BillingSubscriptionId = subscription.BillingSubscriptionId,
                State = subscription.State,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                NextBillingDate = subscription.NextBillingDate,
                ActivatedAt = subscription.ActivatedAt,
                CanceledAt = subscription.CanceledAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Created($"api/my-subscriptions", response);
        }
        catch (UnknownSubscriptionPlanException)
        {
            return Results.NotFound(new ErrorDetails
            {
                StatusCode = StatusCodes.Status404NotFound,
                Message = $"Unknown subscription plan '{request.PlanHandle}'."
            });
        }
    }
}
