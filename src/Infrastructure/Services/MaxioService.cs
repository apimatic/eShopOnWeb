using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Implementation of the Maxio Advanced Billing API client.
/// Uses HTTP Basic Authentication with API key as username and "X" as password.
/// </summary>
public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = _options.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        // Maxio uses HTTP Basic Auth: API key as username, "X" as password
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync()
    {
        _logger.LogInformation("Fetching products from Maxio");

        var response = await _httpClient.GetAsync("/products.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<List<ProductItem>>(json, JsonOptions);

        return result?.ConvertAll(p => p.Product) ?? new List<MaxioProduct>();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle)
    {
        _logger.LogInformation("Fetching product by handle: {Handle}", handle);

        var response = await _httpClient.GetAsync($"/products/lookup.json?handle={Uri.EscapeDataString(handle)}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ProductLookupResponse>(json, JsonOptions);

        return result?.Product;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference)
    {
        _logger.LogInformation("Looking up customer by reference: {Reference}", reference);

        var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CustomerResponse>(json, JsonOptions);

        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        _logger.LogInformation("Creating customer with reference: {Reference}", reference);

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerData
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CustomerResponse>(json, JsonOptions);

        return result?.Customer ?? throw new InvalidOperationException("Failed to deserialize customer response");
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var existing = await GetCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Customer already exists with reference: {Reference}, ID: {Id}", reference, existing.Id);
            return existing;
        }

        return await CreateCustomerAsync(reference, firstName, lastName, email);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        _logger.LogInformation("Creating subscription for customer {CustomerId} with product {ProductHandle}", customerId, productHandle);

        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionData
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/subscriptions.json", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<SubscriptionResponse>(json, JsonOptions);

        return result?.Subscription ?? throw new InvalidOperationException("Failed to deserialize subscription response");
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId)
    {
        _logger.LogInformation("Fetching subscription: {SubscriptionId}", subscriptionId);

        var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<SubscriptionResponse>(json, JsonOptions);

        return result?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsByCustomerReferenceAsync(string customerReference)
    {
        _logger.LogInformation("Fetching subscriptions for customer reference: {CustomerReference}", customerReference);

        var customer = await GetCustomerByReferenceAsync(customerReference);
        if (customer == null)
        {
            return new List<MaxioSubscription>();
        }

        var response = await _httpClient.GetAsync($"/customers/{customer.Id}/subscriptions.json");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new List<MaxioSubscription>();

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<List<SubscriptionItem>>(json, JsonOptions);

        return result?.ConvertAll(s => s.Subscription) ?? new List<MaxioSubscription>();
    }

    #region JSON DTOs

    private class ProductsResponse
    {
        [JsonPropertyName("items")]
        public List<ProductItem>? Items { get; set; }
    }

    private class ProductItem
    {
        [JsonPropertyName("product")]
        public MaxioProduct Product { get; set; } = new();
    }

    private class ProductLookupResponse
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private class CustomerResponse
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class SubscriptionResponse
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }

    private class SubscriptionsResponse
    {
        [JsonPropertyName("items")]
        public List<SubscriptionItem>? Items { get; set; }
    }

    private class SubscriptionItem
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription Subscription { get; set; } = new();
    }

    private class SubscriptionProduct
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("price_in_cents")]
        public long PriceInCents { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("interval_unit")]
        public string IntervalUnit { get; set; } = string.Empty;
    }

    private class CreateCustomerRequest
    {
        [JsonPropertyName("customer")]
        public CreateCustomerData Customer { get; set; } = new();
    }

    private class CreateCustomerData
    {
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    private class CreateSubscriptionRequest
    {
        [JsonPropertyName("subscription")]
        public CreateSubscriptionData Subscription { get; set; } = new();
    }

    private class CreateSubscriptionData
    {
        [JsonPropertyName("customer_id")]
        public int CustomerId { get; set; }

        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; set; } = string.Empty;

        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; set; } = "invoice";
    }

    #endregion
}
