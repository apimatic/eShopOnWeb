using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<List<MaxioProduct>> GetProductsByFamilyHandle(string familyHandle);
    Task<MaxioCustomer?> GetOrCreateCustomer(string email, string firstName, string lastName, string reference);
    Task<MaxioSubscription?> CreateSubscription(int customerId, string productHandle);
    Task<List<MaxioSubscription>> GetSubscriptionsByCustomerId(int customerId);
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
    }

    public async Task<List<MaxioProduct>> GetProductsByFamilyHandle(string familyHandle)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/product_families/lookup.json?handle={familyHandle}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            using var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            var products = new List<MaxioProduct>();
            if (root.TryGetProperty("product_family", out var family) &&
                family.TryGetProperty("products", out var productsArray))
            {
                foreach (var productJson in productsArray.EnumerateArray())
                {
                    var product = JsonSerializer.Deserialize<MaxioProduct>(productJson.GetRawText(), options);
                    if (product != null)
                    {
                        products.Add(product);
                    }
                }
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products for family {FamilyHandle}", familyHandle);
            throw;
        }
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomer(string email, string firstName, string lastName, string reference)
    {
        try
        {
            // Try to find existing customer by reference
            var existingCustomer = await FindCustomerByReference(reference);
            if (existingCustomer != null)
            {
                return existingCustomer;
            }

            // Create new customer
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CustomerAttributes
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            var url = $"{_settings.GetBaseUrl()}/customers.json";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            AddAuthHeader(request);

            var json = JsonSerializer.Serialize(createRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create customer: {StatusCode} {Content}", response.StatusCode, errorContent);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var customerResponse = JsonSerializer.Deserialize<CustomerResponse>(content, options);

            return customerResponse?.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer {Email}", email);
            throw;
        }
    }

    public async Task<MaxioSubscription?> CreateSubscription(int customerId, string productHandle)
    {
        try
        {
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionAttributes
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = "remittance" // No payment method required
                }
            };

            var url = $"{_settings.GetBaseUrl()}/subscriptions.json";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            AddAuthHeader(request);

            var json = JsonSerializer.Serialize(createRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create subscription: {StatusCode} {Content}", response.StatusCode, errorContent);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var subscriptionResponse = JsonSerializer.Deserialize<SubscriptionResponse>(content, options);

            return subscriptionResponse?.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> GetSubscriptionsByCustomerId(int customerId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/subscriptions.json?customer_id={customerId}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            var subscriptions = JsonSerializer.Deserialize<List<SubscriptionResponse>>(content, options) ?? new List<SubscriptionResponse>();
            return subscriptions.Where(s => s.Subscription != null).Select(s => s.Subscription!).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReference(string reference)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return null; // Customer not found
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var customerResponse = JsonSerializer.Deserialize<CustomerResponse>(content, options);

            return customerResponse?.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer by reference");
            return null;
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(_settings.ApiKey))
            throw new InvalidOperationException("Maxio API key is not configured");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
