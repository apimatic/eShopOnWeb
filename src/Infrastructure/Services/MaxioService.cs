using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioService(HttpClient httpClient, IOptions<MaxioSettings> options, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured");
        }
        if (string.IsNullOrEmpty(_settings.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured");
        }

        var baseUrl = _settings.BaseUrl
            ?? $"https://{_settings.Subdomain}.maxio.com/api/v1";

        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<MaxioProductDto> GetProductAsync(string handle)
    {
        var response = await _httpClient.GetAsync($"/products/handle:{handle}.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        if (wrapper.TryGetProperty("product", out var productElement))
        {
            var product = JsonSerializer.Deserialize<MaxioProductDto>(productElement.GetRawText(), _jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize product");
            return product;
        }

        throw new InvalidOperationException("Product not found in response");
    }

    public async Task<IEnumerable<MaxioProductDto>> GetProductsAsync()
    {
        if (string.IsNullOrEmpty(_settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured");
        }

        var response = await _httpClient.GetAsync(
            $"/product_families/handle:{_settings.ProductFamilyHandle}/products.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        var products = new List<MaxioProductDto>();

        if (wrapper.TryGetProperty("products", out var productsElement) && productsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in productsElement.EnumerateArray())
            {
                var product = JsonSerializer.Deserialize<MaxioProductDto>(item.GetRawText(), _jsonOptions);
                if (product != null)
                {
                    products.Add(product);
                }
            }
        }

        return products;
    }

    public async Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        // Try to get existing customer by reference
        try
        {
            var getResponse = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}");
            if (getResponse.IsSuccessStatusCode)
            {
                var content = await getResponse.Content.ReadAsStringAsync();
                var wrapper = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

                if (wrapper.TryGetProperty("customer", out var customerElement))
                {
                    var customer = JsonSerializer.Deserialize<MaxioCustomerDto>(customerElement.GetRawText(), _jsonOptions);
                    if (customer != null)
                    {
                        _logger.LogInformation("Found existing Maxio customer for userId {UserId} (customerId {CustomerId})",
                            userId, customer.Id);
                        return customer;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error looking up customer, will attempt to create new one");
        }

        // Create new customer
        var createRequest = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = userId
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(createRequest),
            Encoding.UTF8,
            "application/json");

        var createResponse = await _httpClient.PostAsync("/customers.json", jsonContent);
        createResponse.EnsureSuccessStatusCode();

        var createContent = await createResponse.Content.ReadAsStringAsync();
        var createWrapper = JsonSerializer.Deserialize<JsonElement>(createContent, _jsonOptions);

        if (createWrapper.TryGetProperty("customer", out var newCustomerElement))
        {
            var customer = JsonSerializer.Deserialize<MaxioCustomerDto>(newCustomerElement.GetRawText(), _jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize created customer");
            _logger.LogInformation("Created new Maxio customer for userId {UserId} (customerId {CustomerId})",
                userId, customer.Id);
            return customer;
        }

        throw new InvalidOperationException("Customer creation failed");
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(string customerReference, string productHandle)
    {
        var createRequest = new
        {
            subscription = new
            {
                customer_reference = customerReference,
                product_handle = productHandle
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(createRequest),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/subscriptions.json", jsonContent);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        if (wrapper.TryGetProperty("subscription", out var subscriptionElement))
        {
            var subscription = JsonSerializer.Deserialize<MaxioSubscriptionDto>(subscriptionElement.GetRawText(), _jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize subscription");
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerReference}",
                subscription.Id, customerReference);
            return subscription;
        }

        throw new InvalidOperationException("Subscription creation failed");
    }

    public async Task<IEnumerable<MaxioSubscriptionDto>> ListSubscriptionsAsync(string customerReference)
    {
        var response = await _httpClient.GetAsync($"/subscriptions.json?customer_reference={Uri.EscapeDataString(customerReference)}");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        var subscriptions = new List<MaxioSubscriptionDto>();

        if (wrapper.TryGetProperty("subscriptions", out var subscriptionsElement) && subscriptionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in subscriptionsElement.EnumerateArray())
            {
                var subscription = JsonSerializer.Deserialize<MaxioSubscriptionDto>(item.GetRawText(), _jsonOptions);
                if (subscription != null)
                {
                    subscriptions.Add(subscription);
                }
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscriptionDto> GetSubscriptionAsync(int subscriptionId)
    {
        var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        if (wrapper.TryGetProperty("subscription", out var subscriptionElement))
        {
            var subscription = JsonSerializer.Deserialize<MaxioSubscriptionDto>(subscriptionElement.GetRawText(), _jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize subscription");
            return subscription;
        }

        throw new InvalidOperationException("Subscription not found");
    }
}
