using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient httpClient,
        MaxioSettings settings,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        _httpClient.BaseAddress = new Uri(_settings.GetApiBaseUrl());
    }

    public async Task<List<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/products.json", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlan>();

            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("product", out var product))
                    {
                        var plan = ParseProductToPlan(product);
                        if (plan != null)
                        {
                            plans.Add(plan);
                        }
                    }
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscription plans from Maxio");
            throw;
        }
    }

    public async Task<(bool Success, string Message, UserSubscription? Subscription)> CreateSubscriptionAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_reference = userId,
                    customer_attributes = new
                    {
                        first_name = firstName,
                        last_name = lastName,
                        email = email,
                        reference = userId
                    }
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create subscription: {StatusCode} {Body}", response.StatusCode, responseBody);
                return (false, $"Failed to create subscription: {response.StatusCode}", null);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscription))
            {
                var userSub = ParseSubscription(subscription);
                return (true, "Subscription created successfully", userSub);
            }

            return (false, "Invalid response format from Maxio", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio for user {UserId}", userId);
            return (false, $"Error: {ex.Message}", null);
        }
    }

    public async Task<List<UserSubscription>> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(userId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new List<UserSubscription>();
                }
                return new List<UserSubscription>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var subscriptions = new List<UserSubscription>();

            if (root.TryGetProperty("subscription", out var subscription))
            {
                var userSub = ParseSubscription(subscription);
                subscriptions.Add(userSub);
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscriptions for user {UserId}", userId);
            return new List<UserSubscription>();
        }
    }

    private SubscriptionPlan? ParseProductToPlan(JsonElement product)
    {
        try
        {
            var plan = new SubscriptionPlan();

            if (product.TryGetProperty("id", out var id))
                plan.Id = id.GetInt32();

            if (product.TryGetProperty("handle", out var handle))
                plan.Handle = handle.GetString() ?? string.Empty;

            if (product.TryGetProperty("name", out var name))
                plan.Name = name.GetString() ?? string.Empty;

            if (product.TryGetProperty("description", out var description))
                plan.Description = description.GetString();

            if (product.TryGetProperty("price_in_cents", out var price))
                plan.PriceInCents = price.GetInt64();

            if (product.TryGetProperty("interval", out var interval))
                plan.Interval = interval.GetInt32();

            if (product.TryGetProperty("interval_unit", out var intervalUnit))
                plan.IntervalUnit = intervalUnit.GetString() ?? "month";

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing product to plan");
            return null;
        }
    }

    private UserSubscription ParseSubscription(JsonElement subscription)
    {
        var userSub = new UserSubscription();

        if (subscription.TryGetProperty("id", out var id))
            userSub.MaxioSubscriptionId = id.GetInt32();

        if (subscription.TryGetProperty("state", out var state))
            userSub.State = state.GetString();

        if (subscription.TryGetProperty("current_period_ends_at", out var endsAt))
        {
            if (endsAt.ValueKind == JsonValueKind.String)
                userSub.CurrentPeriodEndsAt = DateTime.Parse(endsAt.GetString() ?? string.Empty);
        }

        if (subscription.TryGetProperty("next_assessment_at", out var nextAssess))
        {
            if (nextAssess.ValueKind == JsonValueKind.String)
                userSub.NextAssessmentAt = DateTime.Parse(nextAssess.GetString() ?? string.Empty);
        }

        if (subscription.TryGetProperty("product", out var product))
        {
            if (product.TryGetProperty("handle", out var handle))
                userSub.ProductHandle = handle.GetString();

            if (product.TryGetProperty("name", out var name))
                userSub.ProductName = name.GetString();

            if (product.TryGetProperty("price_in_cents", out var price))
                userSub.ProductPriceInCents = price.GetInt64();
        }

        return userSub;
    }
}
