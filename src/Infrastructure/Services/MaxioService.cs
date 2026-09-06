using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioService
{
    Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync();
    Task<(long CustomerId, long SubscriptionId)> CreateOrGetSubscriptionAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle);
    Task<List<(long Id, string ProductHandle, string State, DateTime? NextBillingAt)>> GetSubscriptionsAsync(string userId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioService> _logger;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _productFamilyHandle;

    public MaxioService(IConfiguration configuration, ILogger<MaxioService> logger)
    {
        _logger = logger;
        _apiKey = configuration["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey is required");
        var subdomain = configuration["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain is required");
        var baseUrl = configuration["Maxio:BaseUrl"];
        _productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle is required");

        if (!string.IsNullOrEmpty(baseUrl))
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }
        else
        {
            _baseUrl = $"https://{subdomain}.chargify.com";
        }

        _httpClient = new HttpClient();
        SetAuthHeader();
    }

    private void SetAuthHeader()
    {
        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_apiKey}:"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authString}");
    }

    public async Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync()
    {
        var plans = new List<SubscriptionPlan>();

        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/products.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using (JsonDocument doc = JsonDocument.Parse(content))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("products", out var productsArray))
                {
                    foreach (var product in productsArray.EnumerateArray())
                    {
                        var productFamily = product.GetProperty("product_family");
                        if (productFamily.TryGetProperty("handle", out var familyHandle) &&
                            familyHandle.GetString() == _productFamilyHandle)
                        {
                            plans.Add(new SubscriptionPlan
                            {
                                Handle = product.GetProperty("handle").GetString() ?? "",
                                Name = product.GetProperty("name").GetString() ?? "",
                                Description = product.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                                PriceInCents = product.TryGetProperty("price_in_cents", out var price) && price.ValueKind != JsonValueKind.Null ? price.GetInt64() : 0,
                                Interval = product.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 0,
                                IntervalUnit = product.TryGetProperty("interval_unit", out var unit) ? unit.GetString() ?? "month" : "month"
                            });
                        }
                    }
                }
            }

            _logger.LogInformation($"Retrieved {plans.Count} subscription plans from Maxio");
            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscription plans from Maxio");
            throw;
        }
    }

    public async Task<(long CustomerId, long SubscriptionId)> CreateOrGetSubscriptionAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle)
    {
        try
        {
            var customerReference = GenerateCustomerReference(userId);

            var customerId = await GetOrCreateCustomerAsync(customerReference, firstName, lastName, email);

            var productId = await GetProductIdByHandleAsync(productHandle);

            var existingSubscriptionId = await GetExistingActiveSubscriptionIdAsync(customerId, productHandle);
            if (existingSubscriptionId.HasValue)
            {
                _logger.LogInformation($"Found existing subscription {existingSubscriptionId} for customer {customerId}");
                return (customerId, existingSubscriptionId.Value);
            }

            var subscriptionId = await CreateSubscriptionAsync(customerId, productId);

            _logger.LogInformation($"Created new subscription {subscriptionId} for customer {customerId}");
            return (customerId, subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating/getting subscription for {userId}");
            throw;
        }
    }

    public async Task<List<(long Id, string ProductHandle, string State, DateTime? NextBillingAt)>> GetSubscriptionsAsync(string userId)
    {
        var subscriptions = new List<(long, string, string, DateTime?)>();

        try
        {
            var customerReference = GenerateCustomerReference(userId);

            var customerId = await GetCustomerIdByReferenceAsync(customerReference);
            if (!customerId.HasValue)
            {
                return subscriptions;
            }

            var response = await _httpClient.GetAsync($"{_baseUrl}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using (JsonDocument doc = JsonDocument.Parse(content))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("subscriptions", out var subsArray))
                {
                    foreach (var sub in subsArray.EnumerateArray())
                    {
                        if (sub.TryGetProperty("customer_id", out var custId) && custId.GetInt64() == customerId.Value)
                        {
                            var subId = sub.GetProperty("id").GetInt64();
                            var product = sub.GetProperty("product");
                            var handle = product.GetProperty("handle").GetString() ?? "";
                            var state = sub.GetProperty("state").GetString() ?? "";
                            var nextBillingAtStr = sub.TryGetProperty("next_billing_at", out var nextBilling) && nextBilling.ValueKind != JsonValueKind.Null
                                ? nextBilling.GetString()
                                : null;

                            DateTime? nextBillingAt = null;
                            if (nextBillingAtStr != null && DateTime.TryParse(nextBillingAtStr, out var parsed))
                            {
                                nextBillingAt = parsed;
                            }

                            subscriptions.Add((subId, handle, state, nextBillingAt));
                        }
                    }
                }
            }

            _logger.LogInformation($"Retrieved {subscriptions.Count} subscriptions for {userId}");
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving subscriptions for {userId}");
            throw;
        }
    }

    private async Task<long> GetOrCreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var existingId = await GetCustomerIdByReferenceAsync(reference);
        if (existingId.HasValue)
        {
            _logger.LogInformation($"Found existing Maxio customer with reference {reference}: {existingId}");
            return existingId.Value;
        }

        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/customers.json", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        using (JsonDocument doc = JsonDocument.Parse(responseContent))
        {
            var customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt64();
            _logger.LogInformation($"Created new Maxio customer with reference {reference}: {customerId}");
            return customerId;
        }
    }

    private async Task<long?> GetCustomerIdByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using (JsonDocument doc = JsonDocument.Parse(content))
            {
                var customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt64();
                return customerId;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<long> GetProductIdByHandleAsync(string handle)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/products.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using (JsonDocument doc = JsonDocument.Parse(content))
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("products", out var productsArray))
            {
                foreach (var product in productsArray.EnumerateArray())
                {
                    if (product.GetProperty("handle").GetString() == handle)
                    {
                        return product.GetProperty("id").GetInt64();
                    }
                }
            }
        }

        throw new InvalidOperationException($"Product with handle '{handle}' not found");
    }

    private async Task<long?> GetExistingActiveSubscriptionIdAsync(long customerId, string productHandle)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/subscriptions.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using (JsonDocument doc = JsonDocument.Parse(content))
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("subscriptions", out var subsArray))
            {
                foreach (var sub in subsArray.EnumerateArray())
                {
                    if (sub.TryGetProperty("customer_id", out var custId) && custId.GetInt64() == customerId)
                    {
                        var product = sub.GetProperty("product");
                        if (product.GetProperty("handle").GetString() == productHandle)
                        {
                            var state = sub.GetProperty("state").GetString();
                            if (state == "active" || state == "pending")
                            {
                                return sub.GetProperty("id").GetInt64();
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private async Task<long> CreateSubscriptionAsync(long customerId, long productId)
    {
        var payload = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_id = productId,
                auto_resume = true
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/subscriptions.json", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        using (JsonDocument doc = JsonDocument.Parse(responseContent))
        {
            var subscriptionId = doc.RootElement.GetProperty("subscription").GetProperty("id").GetInt64();
            return subscriptionId;
        }
    }

    private static string GenerateCustomerReference(string userId)
    {
        return $"eshop-{userId}";
    }
}

public class SubscriptionPlan
{
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";

    public decimal GetPriceInDollars() => PriceInCents / 100m;
}
