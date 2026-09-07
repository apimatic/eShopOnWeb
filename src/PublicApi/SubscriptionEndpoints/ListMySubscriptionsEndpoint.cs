using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioApiClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(MaxioApiClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithName("ListMySubscriptions")
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.NotFound(new { error = "User not found" });
        }

        var customerId = await FindMaxioCustomer(userId);
        if (customerId == null)
        {
            return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = new() });
        }

        var subscriptionsResponse = await _maxioClient.GetAsync<SubscriptionsListResponse>(
            $"/subscriptions.json?customer_id={customerId}"
        );

        var response = new ListMySubscriptionsResponse();
        if (subscriptionsResponse?.Subscriptions != null)
        {
            response.Subscriptions = subscriptionsResponse.Subscriptions
                .Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ProductName = s.Product?.Name ?? string.Empty,
                    ProductHandle = s.Product?.Handle ?? string.Empty,
                    ProductPriceInDollars = (s.Product?.PriceInCents ?? 0) / 100m,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    ActivatedAt = s.ActivatedAt,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt
                })
                .ToList();
        }

        return Results.Ok(response);
    }

    private async Task<int?> FindMaxioCustomer(string reference)
    {
        var lookupResponse = await _maxioClient.PostAsync<CustomerLookupResponse>(
            "/customers/lookup.json",
            new { reference = reference }
        );

        return lookupResponse?.Customer?.Id;
    }

    private class CustomerLookupResponse
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class MaxioCustomer
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    private class SubscriptionsListResponse
    {
        [JsonPropertyName("subscriptions")]
        public List<MaxioSubscription>? Subscriptions { get; set; }
    }

    private class MaxioSubscription
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("current_period_ends_at")]
        public DateTime CurrentPeriodEndsAt { get; set; }

        [JsonPropertyName("next_assessment_at")]
        public DateTime NextAssessmentAt { get; set; }

        [JsonPropertyName("activated_at")]
        public DateTime ActivatedAt { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private class MaxioProduct
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;

        [JsonPropertyName("price_in_cents")]
        public int? PriceInCents { get; set; }
    }
}

public class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
