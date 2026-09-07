using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioBillingService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioBillingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var maxioSection = configuration.GetSection("Maxio");
        _apiKey = maxioSection["ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey not configured");
        var subdomain = maxioSection["Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain not configured");
        _productFamilyHandle = maxioSection["ProductFamilyHandle"] ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle not configured");

        var baseUrlOverride = maxioSection["BaseUrl"];
        _baseUrl = !string.IsNullOrEmpty(baseUrlOverride)
            ? baseUrlOverride
            : $"https://{subdomain}.chargify.com";

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:x"))}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioProduct> GetProductByHandleAsync(string handle)
    {
        try
        {
            var url = $"{_baseUrl}/product_families/handle:{_productFamilyHandle}/products/handle:{handle}.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var productJson = doc.RootElement.GetProperty("product");

            return JsonSerializer.Deserialize<MaxioProduct>(productJson.GetRawText(), JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize product");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product by handle: {Handle}", handle);
            throw;
        }
    }

    public async Task<List<MaxioProduct>> GetProductsForFamilyAsync()
    {
        try
        {
            var url = $"{_baseUrl}/product_families/handle:{_productFamilyHandle}/products.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var products = new List<MaxioProduct>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var productJson = item.GetProperty("product");
                var product = JsonSerializer.Deserialize<MaxioProduct>(productJson.GetRawText(), JsonOptions);
                if (product != null)
                {
                    products.Add(product);
                }
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products for family");
            throw;
        }
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        try
        {
            // Try to get existing customer by reference
            var getUrl = $"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var getResponse = await _httpClient.GetAsync(getUrl);

            if (getResponse.IsSuccessStatusCode)
            {
                var content = await getResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var customerJson = doc.RootElement.GetProperty("customer");
                return JsonSerializer.Deserialize<MaxioCustomer>(customerJson.GetRawText(), JsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize customer");
            }

            // Create new customer
            var createUrl = $"{_baseUrl}/customers.json";
            var customerData = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = reference
                }
            };

            var json = JsonSerializer.Serialize(customerData, JsonOptions);
            var content_create = new StringContent(json, Encoding.UTF8, "application/json");

            var createResponse = await _httpClient.PostAsync(createUrl, content_create);
            createResponse.EnsureSuccessStatusCode();

            var responseContent = await createResponse.Content.ReadAsStringAsync();
            using var createDoc = JsonDocument.Parse(responseContent);
            var createdCustomerJson = createDoc.RootElement.GetProperty("customer");
            return JsonSerializer.Deserialize<MaxioCustomer>(createdCustomerJson.GetRawText(), JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize created customer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer for email: {Email}", email);
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions.json";
            var subscriptionData = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(subscriptionData, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var subscriptionJson = doc.RootElement.GetProperty("subscription");
            return JsonSerializer.Deserialize<MaxioSubscription>(subscriptionJson.GetRawText(), JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer: {CustomerId}, product: {ProductHandle}", customerId, productHandle);
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

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var subscriptions = new List<MaxioSubscription>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var subscriptionJson = item.GetProperty("subscription");
                var subscription = JsonSerializer.Deserialize<MaxioSubscription>(subscriptionJson.GetRawText(), JsonOptions);
                if (subscription != null)
                {
                    subscriptions.Add(subscription);
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for customer: {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions/{subscriptionId}.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var subscriptionJson = doc.RootElement.GetProperty("subscription");
            return JsonSerializer.Deserialize<MaxioSubscription>(subscriptionJson.GetRawText(), JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription: {SubscriptionId}", subscriptionId);
            throw;
        }
    }
}
