using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioService
{
    Task<List<MaxioProduct>> ListProductsByFamilyHandleAsync(string familyHandle);
    Task<MaxioCustomer?> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, int productId, string? reference = null);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_settings.GetBaseUrl());
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<MaxioProduct>> ListProductsByFamilyHandleAsync(string familyHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products.json?filter[product_family_id]={familyHandle}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            var products = new List<MaxioProduct>();
            if (result.TryGetProperty("products", out var productsArray))
            {
                foreach (var item in productsArray.EnumerateArray())
                {
                    products.Add(ParseProduct(item));
                }
            }
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products by family handle: {FamilyHandle}", familyHandle);
            throw;
        }
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        try
        {
            var existing = await GetCustomerByReferenceAsync(reference);
            if (existing != null)
            {
                return existing;
            }

            return await CreateCustomerAsync(reference, email, firstName, lastName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer: {Reference}", reference);
            throw;
        }
    }

    private async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers.json?reference={Uri.EscapeDataString(reference)}");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            if (result.TryGetProperty("customers", out var customersArray))
            {
                foreach (var item in customersArray.EnumerateArray())
                {
                    return ParseCustomer(item);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
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

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

        if (result.TryGetProperty("customer", out var customerData))
        {
            return ParseCustomer(customerData);
        }

        throw new InvalidOperationException("Failed to parse customer response");
    }

    public async Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, int productId, string? reference = null)
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_id = productId,
                    payment_collection_method = "automatic",
                    reference = reference
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

            if (result.TryGetProperty("subscription", out var subscriptionData))
            {
                return ParseSubscription(subscriptionData);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            var subscriptions = new List<MaxioSubscription>();
            if (result.TryGetProperty("subscriptions", out var subscriptionsArray))
            {
                foreach (var item in subscriptionsArray.EnumerateArray())
                {
                    var sub = ParseSubscription(item);
                    if (sub != null)
                    {
                        subscriptions.Add(sub);
                    }
                }
            }
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private MaxioProduct ParseProduct(JsonElement element)
    {
        return new MaxioProduct
        {
            Id = GetInt(element, "id"),
            Name = GetString(element, "name"),
            Handle = GetString(element, "handle"),
            Description = GetString(element, "description"),
            PriceInCents = GetLong(element, "price_in_cents"),
            Interval = GetInt(element, "interval"),
            IntervalUnit = GetString(element, "interval_unit")
        };
    }

    private MaxioCustomer ParseCustomer(JsonElement element)
    {
        return new MaxioCustomer
        {
            Id = GetInt(element, "id"),
            Email = GetString(element, "email"),
            FirstName = GetString(element, "first_name"),
            LastName = GetString(element, "last_name"),
            Reference = GetString(element, "reference")
        };
    }

    private MaxioSubscription? ParseSubscription(JsonElement element)
    {
        var id = GetInt(element, "id");
        if (id == 0)
        {
            return null;
        }

        return new MaxioSubscription
        {
            Id = id,
            CustomerId = GetInt(element, "customer_id"),
            ProductId = GetInt(element, "product_id"),
            State = GetString(element, "state"),
            BalanceInCents = GetLong(element, "balance_in_cents"),
            CurrentPeriodEndsAt = GetDateTime(element, "current_period_ends_at"),
            NextAssessmentAt = GetDateTime(element, "next_assessment_at"),
            CreatedAt = GetDateTime(element, "created_at"),
            UpdatedAt = GetDateTime(element, "updated_at")
        };
    }

    private int GetInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value.GetInt32();
        }
        return 0;
    }

    private long GetLong(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt64();
            }
        }
        return 0;
    }

    private string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private DateTime GetDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var dateStr = value.GetString();
            if (DateTime.TryParse(dateStr, out var dt))
            {
                return dt;
            }
        }
        return DateTime.UtcNow;
    }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
