using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the same
/// request returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionUserResolver, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionUserResolver userResolver, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, userResolver, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionUserResolver userResolver, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["planHandle"] = new[] { "A plan handle is required. List available plans with GET /api/subscription-plans." }
            });
        }

        var user = await userResolver.ResolveAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(user, request.PlanHandle.Trim());
            response.Subscription = ToDto(subscription);
            return Results.Created($"/api/my-subscriptions", response);
        }
        catch (SubscriptionPlanNotFoundException)
        {
            return Results.NotFound(new { message = $"Subscription plan '{request.PlanHandle}' was not found." });
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Billing provider error",
                detail: ex.Message);
        }
    }

    internal static SubscriptionDto ToDto(ApplicationCore.Subscriptions.UserSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.PriceInCents / 100m,
            PriceInCents = subscription.PriceInCents,
            Currency = subscription.Currency,
            Balance = subscription.BalanceInCents / 100m,
            NextBillingDateUtc = subscription.NextBillingDateUtc,
            NextAssessmentAtUtc = subscription.NextAssessmentAtUtc,
            ActivatedAtUtc = subscription.ActivatedAtUtc
        };
    }
}