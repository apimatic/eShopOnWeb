using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpointExtensions
{
    public static WebApplication MapSubscriptionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization()
            .WithTags("Subscriptions");

        group.MapGet("/subscription-plans", GetSubscriptionPlans)
            .WithName("GetSubscriptionPlans");

        group.MapPost("/subscriptions", CreateSubscription)
            .WithName("CreateSubscription");

        group.MapGet("/my-subscriptions", GetMySubscriptions)
            .WithName("GetMySubscriptions");

        return app;
    }

    private static async Task<IResult> GetSubscriptionPlans(IMaxioService maxioService)
    {
        try
        {
            var plans = await maxioService.GetSubscriptionPlansAsync("eshop-subscribe");
            return Results.Ok(new
            {
                plans = plans.Select(p => new
                {
                    id = p.Id,
                    handle = p.Handle,
                    name = p.Name,
                    price = p.Price,
                    description = p.Description
                })
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
        IMaxioService maxioService,
        HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await maxioService.CreateSubscriptionAsync(userId, request.PlanHandle);
            return Results.Ok(new
            {
                subscription = new
                {
                    id = subscription.Id,
                    planHandle = subscription.PlanHandle,
                    state = subscription.State,
                    price = subscription.Price,
                    nextBillingAt = subscription.NextBillingAt,
                    currentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                    currentPeriodEndsAt = subscription.CurrentPeriodEndsAt
                }
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetMySubscriptions(
        IMaxioService maxioService,
        HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await maxioService.GetUserSubscriptionsAsync(userId);
            return Results.Ok(new
            {
                subscriptions = subscriptions.Select(s => new
                {
                    id = s.Id,
                    planHandle = s.PlanHandle,
                    state = s.State,
                    price = s.Price,
                    nextBillingAt = s.NextBillingAt,
                    currentPeriodStartsAt = s.CurrentPeriodStartsAt,
                    currentPeriodEndsAt = s.CurrentPeriodEndsAt
                })
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
