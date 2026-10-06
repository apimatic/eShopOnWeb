using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using UserNotFoundException = Microsoft.eShopWeb.Infrastructure.Identity.UserNotFoundException;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in a subscription plan (JWT-authenticated).
/// Idempotent: repeat calls for the same user and plan return the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.PlanHandle), new[] { "PlanHandle is required." } }
            });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(request.UserName, request.PlanHandle);
            response.Subscription = MapSubscription(subscription);
            return Results.Ok(response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: ex.Message,
                title: "Subscription plan not found");
        }
        catch (UserNotFoundException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: ex.Message,
                title: "User not found");
        }
        catch (MaxioApiException ex) when (ex.StatusCode >= 500 || ex.StatusCode == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                detail: "The billing service is temporarily unavailable. Please try again.",
                title: "Billing service unavailable");
        }
    }

    internal static SubscriptionDto MapSubscription(ApplicationCore.Models.Subscription.SubscriptionDetails subscription) =>
        new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            State = subscription.State,
            PlanName = subscription.PlanName,
            PlanHandle = subscription.PlanHandle,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.Price,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            ActivatedAt = subscription.ActivatedAt,
            NextBillingDate = subscription.NextBillingDate,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            CustomerId = subscription.CustomerId
        };
}