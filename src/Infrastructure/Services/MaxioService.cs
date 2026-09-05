using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MaxioService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _productFamilyHandle;

    public MaxioService(HttpClient httpClient, IConfiguration configuration, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _apiKey = configuration["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey is not configured");
        var subdomain = configuration["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain is not configured");
        _productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured");

        _baseUrl = configuration["Maxio:BaseUrl"] ?? $"https://{subdomain}.chargify.com";

        _httpClient.BaseAddress = new Uri(_baseUrl);

        var authBytes = Encoding.ASCII.GetBytes($"{_apiKey}:x");
        var authHeader = Convert.ToBase64String(authBytes);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authHeader}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
    }

    public async Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products.json", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var plans = new List<MaxioSubscriptionPlan>();

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("products", out var productsArray))
            {
                _logger.LogWarning("No products found in Maxio response");
                return Array.Empty<MaxioSubscriptionPlan>();
            }

            foreach (var product in productsArray.EnumerateArray())
            {
                if (product.TryGetProperty("family_id", out var familyElem))
                {
                    var familyId = familyElem.GetInt32();
                    if (product.TryGetProperty("id", out var idElem) &&
                        product.TryGetProperty("handle", out var handleElem) &&
                        product.TryGetProperty("name", out var nameElem))
                    {
                        var id = idElem.GetInt32();
                        var handle = handleElem.GetString() ?? string.Empty;
                        var name = nameElem.GetString() ?? string.Empty;
                        var description = product.TryGetProperty("description", out var descElem) ? descElem.GetString() ?? string.Empty : string.Empty;

                        decimal price = 0;
                        if (product.TryGetProperty("default_price_point_id", out var pricePointIdElem))
                        {
                            var pricePointId = pricePointIdElem.GetInt32();
                            price = await GetPriceForProductAsync(id, pricePointId, cancellationToken);
                        }

                        plans.Add(new MaxioSubscriptionPlan(id, handle, name, description, price, "monthly"));
                    }
                }
            }

            return plans.ToArray();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(string userId, string email, int planId, CancellationToken cancellationToken = default)
    {
        try
        {
            var customerId = await GetOrCreateCustomerAsync(userId, email, cancellationToken);

            var subscriptionRequest = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_id = planId
                }
            };

            var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", subscriptionRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscription))
            {
                var subscriptionId = subscription.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : 0;
                var status = subscription.TryGetProperty("state", out var statusElem) ? statusElem.GetString() ?? "active" : "active";
                var nextBillingDate = default(DateTime?);

                if (subscription.TryGetProperty("next_billing_at", out var dateElem) && dateElem.ValueKind != JsonValueKind.Null)
                {
                    var dateStr = dateElem.GetString();
                    if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
                    {
                        nextBillingDate = parsedDate;
                    }
                }

                return new MaxioSubscriptionResponse(subscriptionId, customerId, status, nextBillingDate);
            }

            throw new InvalidOperationException("Unexpected Maxio response format");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription[]> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var customerId = await GetCustomerIdAsync(userId, cancellationToken);
            if (customerId == 0)
            {
                return Array.Empty<MaxioSubscription>();
            }

            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var subscriptions = new List<MaxioSubscription>();

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscriptions", out var subscriptionArray))
            {
                foreach (var sub in subscriptionArray.EnumerateArray())
                {
                    var id = sub.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : 0;
                    var custId = sub.TryGetProperty("customer_id", out var custIdElem) ? custIdElem.GetInt32() : customerId;
                    var productId = sub.TryGetProperty("product_id", out var prodIdElem) ? prodIdElem.GetInt32() : 0;
                    var status = sub.TryGetProperty("state", out var statusElem) ? statusElem.GetString() ?? "unknown" : "unknown";

                    var createdAt = DateTime.UtcNow;
                    if (sub.TryGetProperty("created_at", out var createdElem) && !string.IsNullOrEmpty(createdElem.GetString()) && DateTime.TryParse(createdElem.GetString(), out var parsedCreated))
                    {
                        createdAt = parsedCreated;
                    }

                    DateTime? canceledAt = null;
                    if (sub.TryGetProperty("canceled_at", out var canceledElem) && canceledElem.ValueKind != JsonValueKind.Null && !string.IsNullOrEmpty(canceledElem.GetString()) && DateTime.TryParse(canceledElem.GetString(), out var parsedCanceled))
                    {
                        canceledAt = parsedCanceled;
                    }

                    DateTime? nextBillingDate = null;
                    if (sub.TryGetProperty("next_billing_at", out var nextBillingElem) && nextBillingElem.ValueKind != JsonValueKind.Null && !string.IsNullOrEmpty(nextBillingElem.GetString()) && DateTime.TryParse(nextBillingElem.GetString(), out var parsedNextBilling))
                    {
                        nextBillingDate = parsedNextBilling;
                    }

                    var price = 0m;
                    if (sub.TryGetProperty("current_period_ends_at", out _))
                    {
                        price = await GetSubscriptionPriceAsync(id, cancellationToken);
                    }

                    subscriptions.Add(new MaxioSubscription(id, custId, productId, status, createdAt, canceledAt, nextBillingDate, price));
                }
            }

            return subscriptions.ToArray();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions from Maxio");
            throw;
        }
    }

    private async Task<int> GetOrCreateCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existingCustomerId = await GetCustomerIdAsync(userId, cancellationToken);
        if (existingCustomerId != 0)
        {
            return existingCustomerId;
        }

        var firstName = userId.Split("@")[0];
        var customerRequest = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = "Subscriber",
                email = email,
                reference = userId
            }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/customers.json", customerRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var idElem))
            {
                return idElem.GetInt32();
            }

            throw new InvalidOperationException("Failed to create customer in Maxio");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("422"))
        {
            _logger.LogWarning("Customer creation returned 422, likely duplicate reference. Retrying lookup...");
            await Task.Delay(500, cancellationToken);
            var retryId = await GetCustomerIdAsync(userId, cancellationToken);
            if (retryId != 0)
                return retryId;
            throw;
        }
    }

    private async Task<int> GetCustomerIdAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return 0;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var idElem))
            {
                return idElem.GetInt32();
            }

            return 0;
        }
        catch (HttpRequestException)
        {
            return 0;
        }
    }

    private async Task<decimal> GetPriceForProductAsync(int productId, int pricePointId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products/{productId}/price_points/{pricePointId}.json", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return 0;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("price_point", out var pricePoint) && pricePoint.TryGetProperty("price", out var priceElem))
            {
                var priceStr = priceElem.GetString();
                if (decimal.TryParse(priceStr, out var price))
                    return price;
            }

            return 0;
        }
        catch (HttpRequestException)
        {
            return 0;
        }
    }

    private async Task<decimal> GetSubscriptionPriceAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return 0;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var sub) && sub.TryGetProperty("snapshot", out var snapshot))
            {
                if (snapshot.TryGetProperty("price", out var priceElem))
                {
                    var priceStr = priceElem.GetString();
                    if (decimal.TryParse(priceStr, out var price))
                        return price;
                }
            }

            return 0;
        }
        catch (HttpRequestException)
        {
            return 0;
        }
    }
}
