using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<List<ProductDto>> GetProductsAsync(string? familyHandle = null)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/products.json";
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get products: {response.StatusCode}");
                return new List<ProductDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var listResponse = JsonSerializer.Deserialize<ProductListResponse>(content, _jsonOptions);
            return listResponse?.Items ?? new List<ProductDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products from Maxio");
            return new List<ProductDto>();
        }
    }

    public async Task<CustomerDto?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email)
    {
        try
        {
            var customer = new CustomerCreateDto
            {
                First_name = firstName,
                Last_name = lastName,
                Email = email,
                Reference = userId
            };

            var requestBody = new CreateCustomerRequest { Customer = customer };
            var json = JsonSerializer.Serialize(requestBody);
            var url = $"{_settings.GetBaseUrl()}/customers.json";
            var request = CreateRequest(HttpMethod.Post, url, json);

            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK || response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                var content = await response.Content.ReadAsStringAsync();
                var customerResponse = JsonSerializer.Deserialize<CustomerResponse>(content, _jsonOptions);
                return customerResponse?.Customer;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogInformation($"Customer already exists for reference: {userId}");
                var customer_id = await GetCustomerByReferenceAsync(userId);
                if (customer_id.HasValue)
                    return await GetCustomerAsync(customer_id.Value);
            }

            _logger.LogError($"Failed to create/get customer: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/getting customer from Maxio");
            return null;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var subscription = new SubscriptionCreateDto
            {
                Product_handle = productHandle,
                Customer_id = customerId
            };

            var requestBody = new CreateSubscriptionRequest { Subscription = subscription };
            var json = JsonSerializer.Serialize(requestBody);
            var url = $"{_settings.GetBaseUrl()}/subscriptions.json";
            var request = CreateRequest(HttpMethod.Post, url, json);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create subscription: {response.StatusCode} - {errorContent}");
                throw new Exception($"Failed to create subscription: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var subscriptionResponse = JsonSerializer.Deserialize<SubscriptionResponse>(content, _jsonOptions);
            return subscriptionResponse?.Subscription ?? throw new Exception("No subscription in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/subscriptions/{subscriptionId}.json";
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get subscription: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var subscriptionResponse = JsonSerializer.Deserialize<SubscriptionResponse>(content, _jsonOptions);
            return subscriptionResponse?.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription from Maxio");
            return null;
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/{customerId}/subscriptions.json";
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get customer subscriptions: {response.StatusCode}");
                return new List<SubscriptionDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var listResponse = JsonSerializer.Deserialize<SubscriptionListResponse>(content, _jsonOptions);
            return listResponse?.Subscriptions ?? new List<SubscriptionDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer subscriptions from Maxio");
            return new List<SubscriptionDto>();
        }
    }

    private async Task<int?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers.json?reference={Uri.EscapeDataString(reference)}";
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            var customersResponse = JsonSerializer.Deserialize<List<CustomerResponse>>(content, _jsonOptions);
            return customersResponse?.FirstOrDefault()?.Customer?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer by reference from Maxio");
            return null;
        }
    }

    private async Task<CustomerDto?> GetCustomerAsync(int customerId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/{customerId}.json";
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            var customerResponse = JsonSerializer.Deserialize<CustomerResponse>(content, _jsonOptions);
            return customerResponse?.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer from Maxio");
            return null;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, url);

        var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return request;
    }
}
