using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpointExtensions
{
    public static void MapSubscriptionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api")
            .WithTags("SubscriptionEndpoints");

        group.MapGet("/subscription-plans", GetSubscriptionPlans)
            .WithName("ListSubscriptionPlans")
            .AllowAnonymous()
            .Produces<ListSubscriptionPlansResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost("/subscriptions", CreateSubscription)
            .WithName("CreateSubscription")
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet("/my-subscriptions", GetUserSubscriptions)
            .WithName("ListUserSubscriptions")
            .RequireAuthorization()
            .Produces<ListUserSubscriptionsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);
    }

    private static async Task<IResult> GetSubscriptionPlans(ISubscriptionService subscriptionService)
    {
        try
        {
            var plans = await subscriptionService.GetAvailablePlansAsync();

            var response = new ListSubscriptionPlansResponse(Guid.NewGuid())
            {
                Plans = plans.Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    Price = p.PriceInCents / 100m,
                    BillingCycle = $"{p.IntervalCount} {p.Interval}(s)"
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        ClaimsPrincipal user)
    {
        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userEmail = user.FindFirst(ClaimTypes.Email)?.Value;
            var firstName = user.FindFirst("given_name")?.Value ?? "User";
            var lastName = user.FindFirst("family_name")?.Value ?? "Subscription";

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userEmail))
            {
                return Results.BadRequest(new { error = "User information not found in token" });
            }

            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest(new { error = "Plan handle is required" });
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                userId, userEmail, firstName, lastName, request.PlanHandle);

            if (subscription == null)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            var response = new CreateSubscriptionResponse(Guid.NewGuid())
            {
                SubscriptionId = subscription.SubscriptionId,
                State = subscription.State,
                PlanName = subscription.ProductName,
                PlanHandle = subscription.ProductHandle,
                Price = subscription.PriceInCents / 100m,
                BillingCycle = subscription.Interval,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Created($"/api/subscriptions/{subscription.SubscriptionId}", response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetUserSubscriptions(
        ISubscriptionService subscriptionService,
        ClaimsPrincipal user)
    {
        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest(new { error = "User information not found in token" });
            }

            var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId);

            var response = new ListUserSubscriptionsResponse(Guid.NewGuid())
            {
                Subscriptions = subscriptions.Select(s => new SubscriptionDto
                {
                    SubscriptionId = s.SubscriptionId,
                    State = s.State,
                    PlanName = s.ProductName,
                    PlanHandle = s.ProductHandle,
                    Price = s.PriceInCents / 100m,
                    BillingCycle = s.Interval,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    ActivatedAt = s.ActivatedAt,
                    CreatedAt = s.CreatedAt
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
