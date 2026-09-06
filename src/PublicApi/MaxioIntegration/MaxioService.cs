using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public interface IMaxioService
{
    Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<List<MaxioProduct>> ListProductsAsync();
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            var baseUrl = _settings.GetBaseUrl();
            var customersUrl = $"{baseUrl}/customers.json?reference={Uri.EscapeDataString(userId)}";

            var response = await GetAsync(customersUrl);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var listResponse = JsonSerializer.Deserialize<dynamic>(content, _jsonOptions);

                // Check if customer exists
                if (listResponse is not null)
                {
                    // Try to get customer from response
                    try
                    {
                        var customer = JsonSerializer.Deserialize<MaxioCustomersListResponse>(content);
                        if (customer?.Customers?.Count > 0)
                        {
                            return customer.Customers[0];
                        }
                    }
                    catch
                    {
                        // Continue to create new customer
                    }
                }
            }

            // Create new customer
            return await CreateCustomerAsync(userId, email, firstName, lastName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating Maxio customer for userId {UserId}", userId);
            throw;
        }
    }

    public async Task<List<MaxioProduct>> ListProductsAsync()
    {
        try
        {
            var baseUrl = _settings.GetBaseUrl();
            var url = $"{baseUrl}/products.json?family_id={_settings.ProductFamilyHandle}";

            var response = await GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var productsResponse = JsonSerializer.Deserialize<MaxioProductsListResponse>(content, _jsonOptions);

            return productsResponse?.Products ?? new List<MaxioProduct>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products from Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var baseUrl = _settings.GetBaseUrl();
            var url = $"{baseUrl}/subscriptions.json";

            var requestBody = new MaxioCreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionData
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var subscriptionResponse = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseContent, _jsonOptions);

            if (subscriptionResponse?.Subscription == null)
            {
                throw new InvalidOperationException("Failed to parse subscription response from Maxio");
            }

            return subscriptionResponse.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio for customerId {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var baseUrl = _settings.GetBaseUrl();
            var url = $"{baseUrl}/customers/{customerId}/subscriptions.json";

            var response = await GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var listResponse = JsonSerializer.Deserialize<MaxioSubscriptionsListResponse>(content, _jsonOptions);

            return listResponse?.Subscriptions ?? new List<MaxioSubscription>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing customer subscriptions from Maxio for customerId {CustomerId}", customerId);
            throw;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        var baseUrl = _settings.GetBaseUrl();
        var url = $"{baseUrl}/customers.json";

        var requestBody = new MaxioCreateCustomerRequest
        {
            Customer = new CreateCustomerData
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var customerResponse = JsonSerializer.Deserialize<MaxioCustomerResponse>(responseContent, _jsonOptions);

        if (customerResponse?.Customer == null)
        {
            throw new InvalidOperationException("Failed to parse customer response from Maxio");
        }

        return customerResponse.Customer;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        return request;
    }

    private async Task<HttpResponseMessage> GetAsync(string url)
    {
        var request = CreateRequest(HttpMethod.Get, url);
        return await _httpClient.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
    {
        var request = CreateRequest(HttpMethod.Post, url);
        request.Content = content;
        return await _httpClient.SendAsync(request);
    }
}

public class MaxioCustomersListResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("customers")]
    public List<MaxioCustomer> Customers { get; set; } = new();
}
