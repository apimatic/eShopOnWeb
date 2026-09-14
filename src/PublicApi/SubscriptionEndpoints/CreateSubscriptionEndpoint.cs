using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated buyer into a subscription plan. Idempotent:
/// repeat requests return the existing subscription instead of creating a new one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { errors = new[] { "ProductHandle is required." } });
        }

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        // Fail fast with 404 when the plan handle is unknown to the billing system.
        var plans = await subscriptionService.ListPlansAsync();
        if (!plans.Any(p => string.Equals(p.Handle, request.ProductHandle, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.NotFound(new { errors = new[] { $"Subscription plan '{request.ProductHandle}' was not found." } });
        }

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(
                userName,
                request.ProductHandle,
                applicationUser.Email ?? userName,
                userName);

            response.Subscription = new SubscriptionDto(
                subscription.BuyerId, subscription.MaxioSubscriptionId, subscription.ProductHandle,
                subscription.ProductName, subscription.PriceInCents, subscription.Price,
                subscription.Interval, subscription.IntervalUnit, subscription.State, subscription.Currency,
                subscription.NextBillingDate, subscription.ActivatedAt, subscription.CanceledAt,
                subscription.AlreadyExisted);

            return subscription.AlreadyExisted ? Results.Ok(response) : Results.Created("api/my-subscriptions", response);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(statusCode: 502, title: "The billing system rejected the request.",
                detail: string.Join("; ", ex.Errors.Count > 0 ? ex.Errors : new[] { ex.Message }),
                extensions: new Dictionary<string, object?> { ["correlationId"] = request.CorrelationId() });
        }
    }
}
