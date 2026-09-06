using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSettings
{
    public string? Subdomain { get; set; }
    public string? ApiKey { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;
    private readonly string _baseUrl;

    public MaxioClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _baseUrl = string.IsNullOrEmpty(_settings.BaseUrl)
            ? $"https://{_settings.Subdomain}.chargify.com"
            : _settings.BaseUrl;

        var authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authHeader}");
    }

    public async Task<List<MaxioProduct>> GetProductsForFamilyAsync(string productFamilyHandle)
    {
        try
        {
            var url = $"{_baseUrl}/product_families/handle:{productFamilyHandle}/products.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(json))
            {
                var products = new List<MaxioProduct>();
                if (doc.RootElement.TryGetProperty("products", out var productsElement))
                {
                    foreach (var prod in productsElement.EnumerateArray())
                    {
                        if (prod.TryGetProperty("product", out var productElement))
                        {
                            products.Add(new MaxioProduct
                            {
                                Id = productElement.GetProperty("id").GetInt32(),
                                Handle = productElement.GetProperty("handle").GetString() ?? "",
                                Name = productElement.GetProperty("name").GetString() ?? "",
                                PriceInCents = productElement.GetProperty("price_in_cents").GetInt64(),
                                Interval = productElement.GetProperty("interval").GetInt32(),
                                IntervalUnit = productElement.GetProperty("interval_unit").GetString() ?? "month"
                            });
                        }
                    }
                }
                return products;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products for family {ProductFamilyHandle}", productFamilyHandle);
            throw;
        }
    }

    public async Task<int> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{_baseUrl}/customers.json?q={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var firstCustomer = doc.RootElement[0];
                    if (firstCustomer.TryGetProperty("customer", out var customerElement))
                    {
                        return customerElement.GetProperty("id").GetInt32();
                    }
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding customer by reference {Reference}", reference);
            return 0;
        }
    }

    public async Task<int> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            var existingId = await FindCustomerByReferenceAsync(userId);
            if (existingId > 0)
            {
                return existingId;
            }

            var payload = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/customers.json", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("customer", out var customerElement))
                {
                    return customerElement.GetProperty("id").GetInt32();
                }
            }
            throw new InvalidOperationException("Failed to create customer: no customer ID in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/finding customer {UserId}", userId);
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("subscription", out var subElement))
                {
                    return new MaxioSubscription
                    {
                        Id = subElement.GetProperty("id").GetInt32(),
                        CustomerId = subElement.GetProperty("customer_id").GetInt32(),
                        ProductId = subElement.GetProperty("product_id").GetInt32(),
                        State = subElement.GetProperty("state").GetString() ?? "active",
                        NextBillingAt = subElement.GetProperty("next_billing_at").GetString() ?? "",
                        CurrentPriceInCents = subElement.GetProperty("current_price_in_cents").GetInt64()
                    };
                }
            }
            throw new InvalidOperationException("Failed to create subscription: no subscription ID in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId} with product {ProductHandle}", customerId, productHandle);
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"{_baseUrl}/customers/{customerId}/subscriptions.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(json))
            {
                var subscriptions = new List<MaxioSubscription>();
                if (doc.RootElement.TryGetProperty("subscriptions", out var subsElement))
                {
                    foreach (var sub in subsElement.EnumerateArray())
                    {
                        subscriptions.Add(new MaxioSubscription
                        {
                            Id = sub.GetProperty("id").GetInt32(),
                            CustomerId = sub.GetProperty("customer_id").GetInt32(),
                            ProductId = sub.GetProperty("product_id").GetInt32(),
                            State = sub.GetProperty("state").GetString() ?? "active",
                            NextBillingAt = sub.GetProperty("next_billing_at").GetString() ?? "",
                            CurrentPriceInCents = sub.GetProperty("current_price_in_cents").GetInt64()
                        });
                    }
                }
                return subscriptions;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }
}
