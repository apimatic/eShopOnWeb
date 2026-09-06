using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.MaxioModels;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioSettings> _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Customer> GetOrCreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        CancellationToken ct = default)
    {
        try
        {
            // Try to find existing customer by email
            var existingCustomer = await SearchCustomerByEmailAsync(email, ct);
            if (existingCustomer != null)
            {
                _logger.LogInformation("Found existing customer {CustomerId} for email {Email}", existingCustomer.Id, email);
                return existingCustomer;
            }

            // Create new customer
            var request = new CreateCustomerRequest
            {
                Customer = new CustomerData
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = email
                }
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/customers", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var createResponse = JsonSerializer.Deserialize<CreateCustomerResponse>(responseContent);

            if (createResponse?.Customer == null)
                throw new InvalidOperationException("Invalid response from Maxio API");

            _logger.LogInformation("Created customer {CustomerId} for email {Email}", createResponse.Customer.Id, email);

            return createResponse.Customer;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting or creating customer for {Email}", email);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer for {Email}", email);
            throw;
        }
    }

    public async Task<List<Product>> GetProductsAsync(CancellationToken ct = default)
    {
        try
        {
            // Get product family ID from handle - for sandbox, this is typically 3023074 for "eshop-subscribe"
            // In production, you'd cache this or store the mapping
            var productFamilyId = await GetProductFamilyIdAsync(_settings.Value.ProductFamilyHandle, ct);

            if (productFamilyId == null)
                throw new InvalidOperationException($"Product family '{_settings.Value.ProductFamilyHandle}' not found");

            // Get products from the specified product family
            var response = await _httpClient.GetAsync($"/products/{productFamilyId}", ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);

            // Parse the JSON response
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var listResponse = JsonSerializer.Deserialize<ListProductsResponse>(responseContent, options);

            if (listResponse?.Products == null)
                throw new InvalidOperationException("Invalid response from Maxio API");

            _logger.LogInformation("Retrieved {ProductCount} products from family {FamilyId}", listResponse.Products.Count, productFamilyId);

            return listResponse.Products;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error retrieving products");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving products");
            throw;
        }
    }

    private async Task<string?> GetProductFamilyIdAsync(string handle, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/product-families?include=handle", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Parse the response to find the family with matching handle
            using var doc = System.Text.Json.JsonDocument.Parse(responseContent);
            var families = doc.RootElement.GetProperty("product_families");

            foreach (var family in families.EnumerateArray())
            {
                var familyHandle = family.GetProperty("handle").GetString();
                if (familyHandle == handle)
                {
                    return family.GetProperty("id").GetInt64().ToString();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting product family ID for handle {Handle}", handle);
            return null;
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(
        long customerId,
        long productId,
        CancellationToken ct = default)
    {
        try
        {
            var request = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionData
                {
                    CustomerId = customerId,
                    ProductId = productId,
                    PaymentCollectionMethod = "automatic"
                }
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/subscriptions", content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var createResponse = JsonSerializer.Deserialize<CreateSubscriptionResponse>(responseContent);

            if (createResponse?.Subscription == null)
                throw new InvalidOperationException("Invalid response from Maxio API");

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for customer {CustomerId}, product {ProductId}",
                createResponse.Subscription.Id,
                customerId,
                productId);

            return createResponse.Subscription;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error creating subscription for customer {CustomerId}, product {ProductId}",
                customerId,
                productId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error creating subscription for customer {CustomerId}, product {ProductId}",
                customerId,
                productId);
            throw;
        }
    }

    public async Task<Subscription> GetSubscriptionAsync(
        long subscriptionId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/subscriptions/{subscriptionId}?include=customer,product",
                ct);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var getResponse = JsonSerializer.Deserialize<GetSubscriptionResponse>(responseContent);

            if (getResponse?.Subscription == null)
                throw new InvalidOperationException("Invalid response from Maxio API");

            _logger.LogInformation(
                "Retrieved subscription {SubscriptionId}, state: {State}",
                subscriptionId,
                getResponse.Subscription.State);

            return getResponse.Subscription;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error retrieving subscription {SubscriptionId}", subscriptionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscription {SubscriptionId}", subscriptionId);
            throw;
        }
    }

    public async Task<List<Subscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/subscriptions?customer_id={customerId}&include=product",
                ct);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var listResponse = JsonSerializer.Deserialize<ListSubscriptionsResponse>(responseContent, options);

            if (listResponse?.Subscriptions == null)
                throw new InvalidOperationException("Invalid response from Maxio API");

            _logger.LogInformation(
                "Retrieved {SubscriptionCount} subscriptions for customer {CustomerId}",
                listResponse.Subscriptions.Count,
                customerId);

            return listResponse.Subscriptions;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error retrieving subscriptions for customer {CustomerId}", customerId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private async Task<Customer?> SearchCustomerByEmailAsync(
        string email,
        CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/customers?search={Uri.EscapeDataString(email)}",
                ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var searchResponse = JsonSerializer.Deserialize<CustomerSearchResponse>(responseContent);

            return searchResponse?.Customers?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for customer by email");
            return null;
        }
    }
}

public class ListSubscriptionsResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("subscriptions")]
    public List<Subscription> Subscriptions { get; set; } = new();
}
