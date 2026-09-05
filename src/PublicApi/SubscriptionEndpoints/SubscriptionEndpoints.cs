using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpointExtensions
{
    public static void AddSubscriptionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api")
            .WithTags("SubscriptionEndpoints");

        group.MapGet("/subscription-plans", ListSubscriptionPlans)
            .Produces<ListSubscriptionPlansResponse>()
            .WithName("ListSubscriptionPlans");

        group.MapPost("/subscriptions", CreateSubscription)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .RequireAuthorization()
            .WithName("CreateSubscription");

        group.MapGet("/my-subscriptions", ListMySubscriptions)
            .Produces<ListMySubscriptionsResponse>()
            .RequireAuthorization()
            .WithName("ListMySubscriptions");
    }

    private static async Task<IResult> ListSubscriptionPlans(IMaxioService maxioService)
    {
        try
        {
            var plans = await maxioService.ListPlansAsync();
            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    Price = p.GetPrice(),
                    IntervalUnit = p.IntervalUnit
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
        IMaxioService maxioService,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal user)
    {
        try
        {
            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest(new { error = "ProductHandle is required" });
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var appUser = await userManager.FindByIdAsync(userId);
            if (appUser == null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            var customer = await maxioService.GetOrCreateCustomerAsync(
                appUser.Id,
                appUser.Email ?? string.Empty,
                appUser.UserName ?? string.Empty,
                appUser.UserName ?? string.Empty);

            appUser.MaxioCustomerId = customer.Id;
            await userManager.UpdateAsync(appUser);

            var subscription = await maxioService.CreateSubscriptionAsync(
                customer.Id,
                request.ProductHandle);

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductName = subscription.ProductName,
                CreatedAt = subscription.CreatedAt,
                NextBillingAt = subscription.NextBillingAt
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListMySubscriptions(
        IMaxioService maxioService,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal user)
    {
        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var appUser = await userManager.FindByIdAsync(userId);
            if (appUser == null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            var response = new ListMySubscriptionsResponse();

            if (appUser.MaxioCustomerId.HasValue)
            {
                var subscriptions = await maxioService.ListSubscriptionsForCustomerAsync(appUser.MaxioCustomerId.Value);
                response.Subscriptions.AddRange(subscriptions.Select(s => new MySubscriptionDto
                {
                    SubscriptionId = s.Id,
                    State = s.State,
                    ProductName = s.ProductName,
                    CreatedAt = s.CreatedAt,
                    NextBillingAt = s.NextBillingAt
                }));
            }

            return Results.Ok(response);
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
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
}

public class ListMySubscriptionsResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
