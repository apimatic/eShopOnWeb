using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint
{
    private readonly IMaxioService _maxioService;

    public ListSubscriptionPlansEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    private async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();
        var (success, products) = await _maxioService.ListProductsAsync();
        if (!success)
        {
            return Results.BadRequest(new { error = "Failed to fetch subscription plans" });
        }

        response.Plans = new List<SubscriptionPlanDto>();
        foreach (var product in products)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id,
                Handle = product.Handle,
                Name = product.Name,
            });
        }

        return Results.Ok(response);
    }
}

public class CreateSubscriptionEndpoint
{
    private readonly IMaxioService _maxioService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioService maxioService, UserManager<ApplicationUser> userManager)
    {
        _maxioService = maxioService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription")
            .RequireAuthorization();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse();
        var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var (existsSuccess, existingCustomer) = await _maxioService.GetCustomerByExternalIdAsync(user.Id);
        MaxioCustomer? customer = null;

        if (existsSuccess && existingCustomer != null)
        {
            customer = existingCustomer;
        }
        else
        {
            var (createSuccess, newCustomer) = await _maxioService.CreateCustomerAsync(
                user.Id,
                user.Email?.Split('@')[0] ?? "User",
                "Subscriber",
                user.Email ?? "");

            if (!createSuccess || newCustomer == null)
            {
                return Results.BadRequest(new { error = "Failed to create customer in billing system" });
            }

            customer = newCustomer;
        }

        var (subSuccess, subscription) = await _maxioService.CreateSubscriptionAsync(customer.Id, request.ProductId);
        if (!subSuccess || subscription == null)
        {
            return Results.BadRequest(new { error = "Failed to create subscription" });
        }

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            CustomerId = subscription.CustomerId,
            ProductId = subscription.ProductId,
            State = subscription.State,
            CreatedAt = subscription.CreatedAt,
            NextBillingAt = subscription.NextBillingAt,
            CurrentPrice = subscription.CurrentPrice,
        };

        return Results.Ok(response);
    }
}

public class ListMySubscriptionsEndpoint
{
    private readonly IMaxioService _maxioService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IMaxioService maxioService, UserManager<ApplicationUser> userManager)
    {
        _maxioService = maxioService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .WithTags("SubscriptionEndpoints")
            .WithName("ListMySubscriptions")
            .RequireAuthorization();
    }

    private async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var response = new ListMySubscriptionsResponse();
        var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var (success, customer) = await _maxioService.GetCustomerByExternalIdAsync(user.Id);
        if (!success || customer == null)
        {
            response.Subscriptions = new List<SubscriptionDto>();
            return Results.Ok(response);
        }

        var (subSuccess, subscriptions) = await _maxioService.ListCustomerSubscriptionsAsync(customer.Id);
        if (!subSuccess)
        {
            return Results.BadRequest(new { error = "Failed to fetch subscriptions" });
        }

        response.Subscriptions = new List<SubscriptionDto>();
        foreach (var sub in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionDto
            {
                Id = sub.Id,
                CustomerId = sub.CustomerId,
                ProductId = sub.ProductId,
                State = sub.State,
                CreatedAt = sub.CreatedAt,
                NextBillingAt = sub.NextBillingAt,
                CurrentPrice = sub.CurrentPrice,
            });
        }

        return Results.Ok(response);
    }
}

// Requests
public class CreateSubscriptionRequest : BaseRequest
{
    public int ProductId { get; set; }
}

// Responses
public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

// DTOs
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal? CurrentPrice { get; set; }
}
