using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization();

        group.MapGet("/subscription-plans", GetSubscriptionPlans)
            .WithName("GetSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");

        group.MapPost("/subscriptions", CreateSubscription)
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");

        group.MapGet("/my-subscriptions", GetMySubscriptions)
            .WithName("GetMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> GetSubscriptionPlans(
        IMaxioBillingService billingService)
    {
        try
        {
            var products = await billingService.GetProductsForFamilyAsync();

            var plans = products
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Price = p.PriceInCents / 100m,
                    Interval = p.Interval.ToString(),
                    IntervalUnit = p.IntervalUnit
                })
                .ToList();

            return Results.Ok(new { plans });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        IMaxioBillingService billingService,
        UserManager<ApplicationUser> userManager,
        CatalogContext catalogContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            var existingSubscription = await catalogContext.Subscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.ProductHandle == request.ProductHandle
                    && (s.State == "active" || s.State == "trialing"));

            if (existingSubscription != null)
            {
                return Results.BadRequest(new { error = "User already has an active subscription for this plan" });
            }

            var maxioCustomer = await billingService.GetOrCreateCustomerAsync(
                user.Email ?? string.Empty,
                user.UserName ?? "User",
                user.UserName ?? "User",
                userId);

            var maxioSubscription = await billingService.CreateSubscriptionAsync(
                request.ProductHandle,
                maxioCustomer.Id);

            var product = await billingService.GetProductByHandleAsync(request.ProductHandle);

            var subscription = new Subscription(
                userId,
                maxioCustomer.Id,
                maxioSubscription.Id,
                request.ProductHandle,
                maxioSubscription.State,
                maxioSubscription.ProductPriceInCents,
                maxioSubscription.CurrentPeriodEndsAt);

            catalogContext.Subscriptions.Add(subscription);
            await catalogContext.SaveChangesAsync();

            var response = new CreateSubscriptionResponse
            {
                Id = subscription.Id,
                MaxioSubscriptionId = maxioSubscription.Id,
                ProductHandle = request.ProductHandle,
                ProductName = product.Name,
                State = maxioSubscription.State,
                Price = product.PriceInCents / 100m,
                CurrentPeriodEndsAt = maxioSubscription.CurrentPeriodEndsAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Ok(response);
        }
        catch (HttpRequestException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetMySubscriptions(
        HttpContext httpContext,
        CatalogContext catalogContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var subscriptions = await catalogContext.Subscriptions
                .Where(s => s.UserId == userId)
                .ToListAsync();

            var response = subscriptions
                .Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    MaxioSubscriptionId = s.MaxioSubscriptionId,
                    ProductHandle = s.ProductHandle,
                    ProductName = s.ProductHandle,
                    State = s.State,
                    Price = s.PriceInCents / 100m,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    CreatedAt = s.CreatedAt
                })
                .ToList();

            return Results.Ok(new { subscriptions = response });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
