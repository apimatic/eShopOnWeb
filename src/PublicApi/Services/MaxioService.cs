using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioService
{
    private readonly MaxioConfiguration _config;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public MaxioService(MaxioConfiguration config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient();
        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var baseUrl = _config.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        try
        {
            _logger.LogInformation("Fetching subscription plans from Maxio");

            var response = await _httpClient.GetAsync("/products.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);

            var plans = new List<SubscriptionPlanDto>();

            if (jsonDoc.RootElement.TryGetProperty("products", out var productsElement))
            {
                foreach (var productElement in productsElement.EnumerateArray())
                {
                    var plan = MapProductToPlan(productElement);
                    if (plan != null)
                    {
                        plans.Add(plan);
                    }
                }
            }

            _logger.LogInformation($"Retrieved {plans.Count} subscription plans");
            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            throw;
        }
    }

    public async Task<SubscriptionDetails> CreateSubscriptionAsync(string userId, string planHandle)
    {
        try
        {
            _logger.LogInformation($"Creating subscription for user {userId} with plan {planHandle}");

            var customerId = await GetOrCreateCustomerAsync(userId);

            var subscriptionPayload = new
            {
                subscription = new
                {
                    product_handle = planHandle,
                    customer_id = long.Parse(customerId)
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(subscriptionPayload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create subscription: {response.StatusCode} - {errorContent}");
                throw new Exception($"Failed to create subscription: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);

            var subscription = MapJsonToSubscription(jsonDoc.RootElement, "subscription");

            _logger.LogInformation($"Successfully created subscription {subscription.Id} for user {userId}");
            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating subscription for user {userId}");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            _logger.LogInformation($"Fetching subscriptions for user {userId}");

            var customerId = await GetCustomerIdAsync(userId);
            if (string.IsNullOrEmpty(customerId))
            {
                _logger.LogWarning($"No Maxio customer found for user {userId}");
                return new List<SubscriptionDto>();
            }

            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to fetch subscriptions: {response.StatusCode}");
                return new List<SubscriptionDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);

            var subscriptions = new List<SubscriptionDto>();

            if (jsonDoc.RootElement.TryGetProperty("subscriptions", out var subsElement))
            {
                foreach (var sub in subsElement.EnumerateArray())
                {
                    var subscription = MapJsonToSubscriptionDto(sub);
                    subscriptions.Add(subscription);
                }
            }

            _logger.LogInformation($"Retrieved {subscriptions.Count} subscriptions for user {userId}");
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching subscriptions for user {userId}");
            throw;
        }
    }

    private async Task<string> GetOrCreateCustomerAsync(string userId)
    {
        var existingId = await GetCustomerIdAsync(userId);
        if (!string.IsNullOrEmpty(existingId))
        {
            return existingId;
        }

        _logger.LogInformation($"Creating new Maxio customer for user {userId}");

        var customerPayload = new
        {
            customer = new
            {
                first_name = "User",
                last_name = userId.Substring(0, Math.Min(userId.Length, 30)),
                email = $"{userId}@eshop.local",
                reference = userId
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(customerPayload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/customers.json", jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Failed to create customer: {response.StatusCode} - {errorContent}");

            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var existingId2 = await GetCustomerIdAsync(userId);
                if (!string.IsNullOrEmpty(existingId2))
                {
                    return existingId2;
                }
            }

            throw new Exception($"Failed to create customer in Maxio: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        if (jsonDoc.RootElement.TryGetProperty("customer", out var customerElement) &&
            customerElement.TryGetProperty("id", out var idElement))
        {
            return idElement.GetString() ?? idElement.GetInt32().ToString();
        }

        throw new Exception("Failed to create customer in Maxio - no ID in response");
    }

    private async Task<string?> GetCustomerIdAsync(string userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={userId}");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Customer lookup failed for userId {userId}: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);

            if (jsonDoc.RootElement.TryGetProperty("customer", out var customerElement) &&
                customerElement.TryGetProperty("id", out var idElement))
            {
                return idElement.GetString() ?? idElement.GetInt32().ToString();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error looking up customer by reference {userId}");
            return null;
        }
    }

    private SubscriptionPlanDto? MapProductToPlan(JsonElement product)
    {
        try
        {
            var plan = new SubscriptionPlanDto();

            if (product.TryGetProperty("id", out var id))
            {
                plan.Id = id.GetString() ?? id.GetInt32().ToString();
            }

            if (product.TryGetProperty("handle", out var handle))
            {
                plan.Handle = handle.GetString() ?? "";
            }

            if (product.TryGetProperty("name", out var name))
            {
                plan.Name = name.GetString() ?? "";
            }

            if (product.TryGetProperty("description", out var description))
            {
                plan.Description = description.GetString() ?? "";
            }

            if (product.TryGetProperty("price_in_cents", out var priceInCents))
            {
                plan.Price = priceInCents.GetInt32() / 100m;
            }

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping product to plan");
            return null;
        }
    }

    private SubscriptionDetails MapJsonToSubscription(JsonElement element, string propertyName)
    {
        var subscription = new SubscriptionDetails();

        if (element.TryGetProperty(propertyName, out var subElement))
        {
            if (subElement.TryGetProperty("id", out var id))
            {
                subscription.Id = id.GetString() ?? id.GetInt32().ToString();
            }

            if (subElement.TryGetProperty("product_handle", out var handle))
            {
                subscription.PlanHandle = handle.GetString() ?? "";
            }

            if (subElement.TryGetProperty("product_name", out var productName))
            {
                subscription.PlanName = productName.GetString() ?? "";
            }

            if (subElement.TryGetProperty("state", out var state))
            {
                subscription.Status = state.GetString() ?? "unknown";
            }

            if (subElement.TryGetProperty("next_billing_at", out var nextBilling))
            {
                if (DateTime.TryParse(nextBilling.GetString(), out var date))
                {
                    subscription.NextBillingDate = date;
                }
            }

            subscription.Price = GetSubscriptionPrice(subElement);
        }

        return subscription;
    }

    private SubscriptionDto MapJsonToSubscriptionDto(JsonElement subElement)
    {
        var subscription = new SubscriptionDto();

        if (subElement.TryGetProperty("id", out var id))
        {
            subscription.Id = id.GetString() ?? id.GetInt32().ToString();
        }

        if (subElement.TryGetProperty("product_handle", out var handle))
        {
            subscription.PlanHandle = handle.GetString() ?? "";
        }

        if (subElement.TryGetProperty("product_name", out var productName))
        {
            subscription.PlanName = productName.GetString() ?? "";
        }

        if (subElement.TryGetProperty("state", out var state))
        {
            subscription.Status = state.GetString() ?? "unknown";
        }

        if (subElement.TryGetProperty("next_billing_at", out var nextBilling))
        {
            if (DateTime.TryParse(nextBilling.GetString(), out var date))
            {
                subscription.NextBillingDate = date;
            }
        }

        subscription.Price = GetSubscriptionPrice(subElement);

        return subscription;
    }

    private decimal GetSubscriptionPrice(JsonElement subElement)
    {
        if (subElement.TryGetProperty("total_revenue_in_cents", out var revenue))
        {
            try
            {
                return revenue.GetInt32() / 100m;
            }
            catch
            {
                return revenue.GetDecimal() / 100m;
            }
        }

        if (subElement.TryGetProperty("current_period_balance_in_cents", out var balance))
        {
            try
            {
                return Math.Abs(balance.GetInt32()) / 100m;
            }
            catch
            {
                return Math.Abs(balance.GetDecimal()) / 100m;
            }
        }

        return 0m;
    }
}

public class SubscriptionPlanDto
{
    public string Id { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly";
    public int? TrialDays { get; set; }
}

public class SubscriptionDetails
{
    public string Id { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
}

public class SubscriptionDto
{
    public string Id { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
}
