using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpointsExtensions
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", ListPlansAsync)
            .Produces<ListPlanResponse>()
            .AllowAnonymous()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions", CreateSubscriptionAsync)
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", ListMySubscriptionsAsync)
            .Produces<ListSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");

        return app;
    }

    private static async Task<IResult> ListPlansAsync(IMaxioSubscriptionService subscriptionService)
    {
        var plans = await subscriptionService.ListPlansAsync();
        var response = new ListPlanResponse
        {
            Plans = plans.Select(p => new PlanDto
            {
                Id = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name,
                Description = p.Description ?? string.Empty,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList()
        };
        return Results.Ok(response);
    }

    private static async Task<IResult> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        IMaxioSubscriptionService subscriptionService,
        HttpContext httpContext)
    {
        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
        {
            return Results.Unauthorized();
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(
            userId, request.FirstName, request.LastName, request.Email, request.ProductHandle);

        var response = new CreateSubscriptionResponse
        {
            Subscription = new SubscriptionDetailDto
            {
                Id = subscription.Id,
                State = subscription.State,
                PlanHandle = subscription.Product?.Handle ?? string.Empty,
                PlanName = subscription.Product?.Name ?? string.Empty,
                PriceInCents = subscription.Product?.PriceInCents ?? 0,
                Interval = subscription.Product?.Interval ?? 0,
                IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            }
        };

        return Results.Created("api/my-subscriptions", response);
    }

    private static async Task<IResult> ListMySubscriptionsAsync(
        IMaxioSubscriptionService subscriptionService,
        HttpContext httpContext)
    {
        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
        {
            return Results.Unauthorized();
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(userId);
        var response = new ListSubscriptionResponse
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDetailDto
            {
                Id = s.Id,
                State = s.State,
                PlanHandle = s.Product?.Handle ?? string.Empty,
                PlanName = s.Product?.Name ?? string.Empty,
                PriceInCents = s.Product?.PriceInCents ?? 0,
                Interval = s.Product?.Interval ?? 0,
                IntervalUnit = s.Product?.IntervalUnit ?? string.Empty,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionDetailDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ListPlanResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

public class CreateSubscriptionResponse
{
    public SubscriptionDetailDto Subscription { get; set; } = null!;
}

public class ListSubscriptionResponse
{
    public List<SubscriptionDetailDto> Subscriptions { get; set; } = new();
}
