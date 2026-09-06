using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Direct HTTP adapter for Maxio API. Use this instead of the SDK due to Maxio SDK v1.0.2 dependency issues.
/// Calls Maxio REST API directly with no external SDK dependency.
/// </summary>
public class MaxioSubscriptionServiceHttpAdapter : IMaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionServiceHttpAdapter> _logger;

    public MaxioSubscriptionServiceHttpAdapter(
        MaxioSettings settings,
        ILogger<MaxioSubscriptionServiceHttpAdapter> logger)
    {
        _settings = settings;
        _logger = logger;

        _httpClient = new HttpClient();
        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var baseUrl = string.IsNullOrEmpty(_settings.BaseUrl)
            ? $"https://{_settings.Subdomain}.chargify.com"
            : _settings.BaseUrl;

        _httpClient.BaseAddress = new Uri(baseUrl);

        // Basic auth: username = API key, password = "x"
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/products.json", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("ListProducts failed: {StatusCode}", response.StatusCode);
                throw new InvalidOperationException($"Failed to list plans: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("products", out var productsArray))
                return new List<SubscriptionPlanDto>();

            var plans = new List<SubscriptionPlanDto>();
            foreach (var product in productsArray.EnumerateArray())
            {
                plans.Add(new SubscriptionPlanDto
                {
                    Id = product.GetProperty("id").GetInt32(),
                    Name = product.GetProperty("name").GetString() ?? string.Empty,
                    Handle = product.GetProperty("handle").GetString() ?? string.Empty,
                    PriceInCents = product.GetProperty("price_in_cents").GetInt64(),
                    Interval = product.GetProperty("interval").GetInt32(),
                    IntervalUnit = product.GetProperty("interval_unit").GetString() ?? "month"
                });
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing plans");
            throw;
        }
    }

    public async Task<SubscriptionPlanDto?> GetPlanByHandleAsync(string handle, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products/handle/{handle}.json", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GetPlanByHandle failed: {StatusCode} for handle {Handle}", response.StatusCode, handle);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var product = doc.RootElement.GetProperty("product");

            return new SubscriptionPlanDto
            {
                Id = product.GetProperty("id").GetInt32(),
                Name = product.GetProperty("name").GetString() ?? string.Empty,
                Handle = product.GetProperty("handle").GetString() ?? string.Empty,
                PriceInCents = product.GetProperty("price_in_cents").GetInt64(),
                Interval = product.GetProperty("interval").GetInt32(),
                IntervalUnit = product.GetProperty("interval_unit").GetString() ?? "month"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting plan by handle: {Handle}", handle);
            throw;
        }
    }

    public async Task<int> EnsureCustomerExistsAsync(string userId, string firstName, string lastName, string email, CancellationToken ct = default)
    {
        try
        {
            // Check if customer already exists by reference
            var checkResponse = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}", ct);
            if (checkResponse.IsSuccessStatusCode)
            {
                var json = await checkResponse.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("customer", out var customer))
                {
                    var customerId = customer.GetProperty("id").GetInt32();
                    _logger.LogInformation("Customer already exists for user {UserId}: {CustomerId}", userId, customerId);
                    return customerId;
                }
            }

            // Create new customer
            var createBody = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(createBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/customers.json", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("CreateCustomer failed: {StatusCode}", response.StatusCode);
                throw new InvalidOperationException("Customer creation failed");
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var responseDoc = JsonDocument.Parse(responseJson);
            var newCustomer = responseDoc.RootElement.GetProperty("customer");
            var newCustomerId = newCustomer.GetProperty("id").GetInt32();

            _logger.LogInformation("Created customer {CustomerId} for user {UserId}", newCustomerId, userId);
            return newCustomerId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring customer exists for user {UserId}", userId);
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var createBody = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(createBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("CreateSubscription failed: {StatusCode}", response.StatusCode);
                throw new InvalidOperationException("Subscription creation failed");
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var responseDoc = JsonDocument.Parse(responseJson);
            var subscription = responseDoc.RootElement.GetProperty("subscription");

            return MapSubscription(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("ListCustomerSubscriptions failed: {StatusCode}", response.StatusCode);
                throw new InvalidOperationException("Failed to list subscriptions");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("subscriptions", out var subscriptionsArray))
                return new List<SubscriptionDto>();

            return subscriptionsArray.EnumerateArray()
                .Select(MapSubscription)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GetSubscription failed: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var subscription = doc.RootElement.GetProperty("subscription");

            return MapSubscription(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription {SubscriptionId}", subscriptionId);
            throw;
        }
    }

    private static SubscriptionDto MapSubscription(JsonElement subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.GetProperty("id").GetInt32(),
            CustomerId = subscription.GetProperty("customer_id").GetInt32(),
            State = subscription.GetProperty("state").GetString() ?? string.Empty,
            ProductPriceInCents = subscription.GetProperty("product_price_in_cents").GetInt64(),
            NextAssessmentAt = subscription.TryGetProperty("next_assessment_at", out var nextAssessment)
                ? DateTimeOffset.Parse(nextAssessment.GetString() ?? string.Empty)
                : null,
            CreatedAt = subscription.TryGetProperty("created_at", out var created)
                ? DateTimeOffset.Parse(created.GetString() ?? string.Empty)
                : null,
            ProductName = subscription.TryGetProperty("product", out var product)
                ? product.GetProperty("name").GetString() ?? string.Empty
                : string.Empty,
            ProductHandle = subscription.TryGetProperty("product", out var product2)
                ? product2.GetProperty("handle").GetString() ?? string.Empty
                : string.Empty
        };
    }
}
