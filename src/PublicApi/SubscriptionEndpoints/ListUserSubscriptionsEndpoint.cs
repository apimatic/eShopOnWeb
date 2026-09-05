using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _maxioService;
    private readonly CatalogContext _catalogContext;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;

    public ListUserSubscriptionsEndpoint(
        IMaxioSubscriptionService maxioService,
        CatalogContext catalogContext,
        ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        _maxioService = maxioService;
        _catalogContext = catalogContext;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", Handle)
            .RequireAuthorization()
            .Produces<ListUserSubscriptionsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithName("ListMySubscriptions")
            .WithTags("SubscriptionEndpoints")
            .WithOpenApi();
    }

    private async Task<IResult> Handle(HttpContext context)
    {
        try
        {
            var ct = context.RequestAborted;
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("ListUserSubscriptionsEndpoint called without user identity");
                return Results.Unauthorized();
            }

            _logger.LogInformation("Retrieving subscriptions for user {UserId}", userId);

            var customerIdKey = $"user:{userId}:maxio-customer-id";
            var subscriptionIdsKey = $"user:{userId}:maxio-subscriptions";

            var maxioCustomerId = GetFromCache(customerIdKey);

            if (maxioCustomerId == null)
            {
                _logger.LogInformation("No cached Maxio customer ID for user {UserId}", userId);

                var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value ?? userId + "@eshop.local";
                var userFirstName = context.User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
                var userLastName = context.User.FindFirst(ClaimTypes.Surname)?.Value ?? userId;

                var customer = await _maxioService.GetOrCreateCustomerAsync(
                    userId,
                    userEmail,
                    userFirstName,
                    userLastName,
                    ct);

                maxioCustomerId = customer.Id;
                StoreInCache(customerIdKey, maxioCustomerId.ToString());
            }

            var subscriptions = await _maxioService.ListSubscriptionsAsync((int)maxioCustomerId, ct);

            var response = new ListUserSubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => new UserSubscriptionResponse
                {
                    SubscriptionId = s.Id,
                    State = s.State,
                    ProductHandle = s.ProductHandle,
                    NextBillingAt = s.NextBillingAt,
                    CreatedAt = s.CreatedAt,
                    CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing user subscriptions");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private string? GetFromCache(string key)
    {
        return null;
    }

    private void StoreInCache(string key, string value)
    {
    }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionResponse> Subscriptions { get; set; } = new();
}

public class UserSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
