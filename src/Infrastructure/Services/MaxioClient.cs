using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioClient
{
    Task<List<ProductDto>> GetProductsAsync();
    Task<CustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<SubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle);
    Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(long customerId);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        var baseUrl = settings.GetBaseUrl();
        if (!_httpClient.BaseAddress?.ToString().Equals(baseUrl, StringComparison.OrdinalIgnoreCase) ?? true)
        {
            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        SetAuthenticationHeader();
    }

    private void SetAuthenticationHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/products.json");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);

            var products = new List<ProductDto>();

            // Maxio returns an array of objects with "product" property
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("product", out var productElement))
                    {
                        products.Add(ParseProductDto(productElement));
                    }
                }
            }
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get products from Maxio");
            throw;
        }
    }

    public async Task<CustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            var requestBody = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/customers.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseContent);

            if (doc.RootElement.TryGetProperty("customer", out var customerElement))
            {
                return ParseCustomerDto(customerElement);
            }

            throw new InvalidOperationException("Customer response missing customer object");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create customer in Maxio");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle)
    {
        try
        {
            var requestBody = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseContent);

            if (doc.RootElement.TryGetProperty("subscription", out var subscriptionElement))
            {
                return ParseSubscriptionDto(subscriptionElement);
            }

            throw new InvalidOperationException("Subscription response missing subscription object");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription in Maxio");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(long customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);

            var subscriptions = new List<SubscriptionDto>();
            if (doc.RootElement.TryGetProperty("subscriptions", out var subscriptionsArray))
            {
                foreach (var subscription in subscriptionsArray.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscriptionDto(subscription));
                }
            }
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get customer subscriptions from Maxio");
            throw;
        }
    }

    private static ProductDto ParseProductDto(JsonElement element)
    {
        var dto = new ProductDto
        {
            Id = GetLongValue(element, "id"),
            Handle = GetStringValue(element, "handle"),
            Name = GetStringValue(element, "name"),
            PriceInCents = GetLongValue(element, "price_in_cents"),
            Interval = GetIntValue(element, "interval"),
            IntervalUnit = GetStringValue(element, "interval_unit")
        };
        return dto;
    }

    private static CustomerDto ParseCustomerDto(JsonElement element)
    {
        var dto = new CustomerDto
        {
            Id = GetLongValue(element, "id"),
            FirstName = GetStringValue(element, "first_name"),
            LastName = GetStringValue(element, "last_name"),
            Email = GetStringValue(element, "email"),
            Reference = GetStringValue(element, "reference"),
            CreatedAt = GetDateTimeValue(element, "created_at")
        };
        return dto;
    }

    private static SubscriptionDto ParseSubscriptionDto(JsonElement element)
    {
        var dto = new SubscriptionDto
        {
            Id = GetLongValue(element, "id"),
            State = GetStringValue(element, "state"),
            CustomerId = GetLongValue(element, "customer_id"),
            ProductHandle = GetStringValue(element, "product_handle"),
            ActivatedAt = GetDateTimeValue(element, "activated_at"),
            NextBillingAt = GetNullableDateTimeValue(element, "next_billing_at"),
            CreatedAt = GetDateTimeValue(element, "created_at"),
            UpdatedAt = GetDateTimeValue(element, "updated_at")
        };

        if (element.TryGetProperty("product", out var product))
        {
            dto.ProductPrice = GetLongValue(product, "price_in_cents") / 100m;
        }

        return dto;
    }

    private static string GetStringValue(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long GetLongValue(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;
    }

    private static int GetIntValue(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    private static DateTime GetDateTimeValue(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString();
            if (DateTime.TryParse(str, out var result))
                return result;
        }
        return DateTime.UtcNow;
    }

    private static DateTime? GetNullableDateTimeValue(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind == JsonValueKind.String)
            {
                var str = value.GetString();
                if (DateTime.TryParse(str, out var result))
                    return result;
            }
        }
        return null;
    }
}

public class ProductDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public decimal GetPrice() => PriceInCents / 100m;
}

public class CustomerDto
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public decimal ProductPrice { get; set; }
}
