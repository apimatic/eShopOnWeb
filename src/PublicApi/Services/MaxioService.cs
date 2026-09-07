using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, IOptions<MaxioConfiguration> options, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _config = options.Value;
        _logger = logger;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        try
        {
            var apiKey = _config.ApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Maxio API Key is not configured");
                return;
            }

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
            _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring Maxio HTTP client");
        }
    }

    public async Task<IEnumerable<SubscriptionPlanDto>> ListProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/products.json");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to list products: {response.StatusCode}");
                return Enumerable.Empty<SubscriptionPlanDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlanDto>();
            if (root.TryGetProperty("products", out var productsArray))
            {
                foreach (var product in productsArray.EnumerateArray())
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.GetProperty("id").GetInt32(),
                        Handle = product.GetProperty("handle").GetString() ?? "",
                        Name = product.GetProperty("name").GetString() ?? "",
                        Description = product.TryGetProperty("description", out var desc) ? (desc.GetString() ?? "") : "",
                        PriceInCents = product.GetProperty("price_in_cents").GetInt64(),
                        Interval = product.GetProperty("interval").GetInt32(),
                        IntervalUnit = product.GetProperty("interval_unit").GetString() ?? ""
                    });
                }
            }
            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products");
            return Enumerable.Empty<SubscriptionPlanDto>();
        }
    }

    public async Task<CustomerDto> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email)
    {
        try
        {
            var existingCustomer = await GetCustomerByReferenceAsync(userId);
            if (existingCustomer != null)
            {
                return existingCustomer;
            }

            var customerPayload = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var json = JsonSerializer.Serialize(customerPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/customers.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create customer: {response.StatusCode} - {errorContent}");
                throw new InvalidOperationException($"Failed to create customer: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customer))
            {
                return new CustomerDto
                {
                    Id = customer.GetProperty("id").GetInt32(),
                    Reference = customer.GetProperty("reference").GetString() ?? "",
                    Email = customer.GetProperty("email").GetString() ?? "",
                    FirstName = customer.GetProperty("first_name").GetString() ?? "",
                    LastName = customer.GetProperty("last_name").GetString() ?? ""
                };
            }

            throw new InvalidOperationException("Invalid response format from Maxio");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating or getting customer");
            throw;
        }
    }

    private async Task<CustomerDto?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                _logger.LogError($"Failed to lookup customer: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customer))
            {
                return new CustomerDto
                {
                    Id = customer.GetProperty("id").GetInt32(),
                    Reference = customer.GetProperty("reference").GetString() ?? "",
                    Email = customer.GetProperty("email").GetString() ?? "",
                    FirstName = customer.GetProperty("first_name").GetString() ?? "",
                    LastName = customer.GetProperty("last_name").GetString() ?? ""
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer by reference");
            return null;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string customerId, string productHandle)
    {
        try
        {
            var subscriptionPayload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle
                }
            };

            var json = JsonSerializer.Serialize(subscriptionPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create subscription: {response.StatusCode} - {errorContent}");
                throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscription))
            {
                return ParseSubscriptionDto(subscription);
            }

            throw new InvalidOperationException("Invalid response format from Maxio");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            throw;
        }
    }

    public async Task<IEnumerable<SubscriptionDto>> ListCustomerSubscriptionsAsync(string customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to list customer subscriptions: {response.StatusCode}");
                return Enumerable.Empty<SubscriptionDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var subscriptions = new List<SubscriptionDto>();
            if (root.TryGetProperty("subscriptions", out var subscriptionsArray))
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
            _logger.LogError(ex, "Error listing customer subscriptions");
            return Enumerable.Empty<SubscriptionDto>();
        }
    }

    private static SubscriptionDto ParseSubscriptionDto(JsonElement subscription)
    {
        var dto = new SubscriptionDto
        {
            Id = subscription.GetProperty("id").GetInt32(),
            CustomerId = subscription.GetProperty("customer_id").GetInt32(),
            ProductId = subscription.TryGetProperty("product_id", out var productId) && !productId.ValueKind.Equals(JsonValueKind.Null)
                ? productId.GetInt32()
                : 0,
            ProductHandle = subscription.TryGetProperty("product", out var product) && product.TryGetProperty("handle", out var handle)
                ? (handle.GetString() ?? "")
                : "",
            ProductName = subscription.TryGetProperty("product", out var product2) && product2.TryGetProperty("name", out var name)
                ? (name.GetString() ?? "")
                : "",
            State = subscription.GetProperty("state").GetString() ?? "",
            ActivatedAt = ParseDateTime(subscription, "activated_at"),
            NextBillingAt = ParseDateTime(subscription, "next_billing_at"),
            CurrentPeriodAmountInCents = subscription.TryGetProperty("current_period_amount_in_cents", out var amount)
                ? amount.GetInt64()
                : null
        };

        return dto;
    }

    private static DateTime? ParseDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && !value.ValueKind.Equals(JsonValueKind.Null))
        {
            var dateString = value.GetString();
            if (DateTime.TryParse(dateString, out var dt))
            {
                return dt;
            }
        }
        return null;
    }
}
