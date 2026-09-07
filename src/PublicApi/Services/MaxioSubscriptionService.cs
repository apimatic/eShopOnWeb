using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string email, string planHandle);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, string email);
    Task<SubscriptionDto?> GetSubscriptionAsync(long subscriptionId);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public MaxioSubscriptionService(
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger,
        HttpClient httpClient)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClient = httpClient;

        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            _baseUrl = _settings.BaseUrl.TrimEnd('/');
        }
        else
        {
            _baseUrl = $"https://{_settings.Subdomain}.chargify.com";
        }

        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authValue}");
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        try
        {
            var url = $"{_baseUrl}/products.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var products = doc.RootElement.GetProperty("products");

            var plans = new List<SubscriptionPlanDto>();

            foreach (var product in products.EnumerateArray())
            {
                var handle = product.GetProperty("handle").GetString() ?? string.Empty;

                if (handle.StartsWith("eshop-"))
                {
                    var plan = new SubscriptionPlanDto
                    {
                        Id = product.GetProperty("id").GetInt64(),
                        Handle = handle,
                        Name = product.GetProperty("name").GetString() ?? string.Empty,
                        Description = product.TryGetProperty("description", out var desc)
                            ? desc.GetString() ?? string.Empty
                            : string.Empty,
                        Price = GetDecimalFromProperty(product, "price_in_cents") / 100,
                        PriceInCents = product.GetProperty("price_in_cents").GetInt64(),
                        Interval = product.TryGetProperty("interval", out var interval)
                            ? interval.GetInt32()
                            : 1,
                        IntervalUnit = product.TryGetProperty("interval_unit", out var unit)
                            ? unit.GetString() ?? "month"
                            : "month"
                    };
                    plans.Add(plan);
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription plans from Maxio");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string email, string planHandle)
    {
        try
        {
            var customerReference = $"eshop-{userId}";
            var customer = await GetOrCreateCustomerAsync(userId, email, customerReference);

            var subscriptionPayload = new
            {
                subscription = new
                {
                    customer_id = customer.Id,
                    product_handle = planHandle,
                    auto_resume = true
                }
            };

            var url = $"{_baseUrl}/subscriptions.json";
            var json = JsonSerializer.Serialize(subscriptionPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var subscription = doc.RootElement.GetProperty("subscription");

            return MapSubscriptionDto(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, string email)
    {
        try
        {
            var customerReference = $"eshop-{userId}";

            try
            {
                var customer = await GetCustomerAsync(customerReference);
                if (customer == null)
                {
                    return new List<SubscriptionDto>();
                }

                var url = $"{_baseUrl}/customers/{customer.Id}/subscriptions.json";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var subscriptions = doc.RootElement.GetProperty("subscriptions");

                var result = new List<SubscriptionDto>();
                foreach (var sub in subscriptions.EnumerateArray())
                {
                    result.Add(MapSubscriptionDto(sub));
                }

                return result;
            }
            catch (HttpRequestException)
            {
                return new List<SubscriptionDto>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for user {UserId}", userId);
            throw;
        }
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(long subscriptionId)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions/{subscriptionId}.json";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var subscription = doc.RootElement.GetProperty("subscription");

            return MapSubscriptionDto(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription {SubscriptionId}", subscriptionId);
            throw;
        }
    }

    private async Task<CustomerDto?> GetCustomerAsync(string reference)
    {
        try
        {
            var url = $"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var customer = doc.RootElement.GetProperty("customer");

            return new CustomerDto
            {
                Id = customer.GetProperty("id").GetInt64(),
                FirstName = customer.GetProperty("first_name").GetString() ?? string.Empty,
                LastName = customer.GetProperty("last_name").GetString() ?? string.Empty,
                Email = customer.GetProperty("email").GetString() ?? string.Empty,
                Reference = customer.GetProperty("reference").GetString() ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<CustomerDto> GetOrCreateCustomerAsync(
        string userId,
        string email,
        string reference)
    {
        var existing = await GetCustomerAsync(reference);
        if (existing != null)
        {
            return existing;
        }

        var customerPayload = new
        {
            customer = new
            {
                first_name = "User",
                last_name = userId,
                email = email,
                reference = reference
            }
        };

        var url = $"{_baseUrl}/customers.json";
        var json = JsonSerializer.Serialize(customerPayload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseContent);
        var customer = doc.RootElement.GetProperty("customer");

        return new CustomerDto
        {
            Id = customer.GetProperty("id").GetInt64(),
            FirstName = customer.GetProperty("first_name").GetString() ?? string.Empty,
            LastName = customer.GetProperty("last_name").GetString() ?? string.Empty,
            Email = customer.GetProperty("email").GetString() ?? string.Empty,
            Reference = customer.GetProperty("reference").GetString() ?? string.Empty
        };
    }

    private SubscriptionDto MapSubscriptionDto(JsonElement subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.GetProperty("id").GetInt64(),
            CustomerId = subscription.GetProperty("customer_id").GetInt64(),
            ProductHandle = subscription.GetProperty("product_handle").GetString() ?? string.Empty,
            ProductName = subscription.TryGetProperty("product_name", out var pn)
                ? pn.GetString() ?? string.Empty
                : string.Empty,
            State = subscription.GetProperty("state").GetString() ?? string.Empty,
            CurrentPeriodStartsAt = GetDateTimeFromProperty(subscription, "current_period_starts_at"),
            CurrentPeriodEndsAt = GetDateTimeFromProperty(subscription, "current_period_ends_at"),
            NextAssessmentAt = GetDateTimeFromProperty(subscription, "next_assessment_at"),
            CreatedAt = GetDateTimeFromProperty(subscription, "created_at"),
            UpdatedAt = GetDateTimeFromProperty(subscription, "updated_at")
        };
    }

    private static DateTime? GetDateTimeFromProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind != JsonValueKind.Null &&
            DateTime.TryParse(prop.GetString(), out var dt))
        {
            return dt;
        }
        return null;
    }

    private static decimal GetDecimalFromProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetDecimal();
            }
            if (prop.ValueKind == JsonValueKind.String &&
                decimal.TryParse(prop.GetString(), out var value))
            {
                return value;
            }
        }
        return 0;
    }

    private class CustomerDto
    {
        public long Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
