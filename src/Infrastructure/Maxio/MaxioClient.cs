using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

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
        _httpClient.BaseAddress = new Uri(baseUrl);

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<ProductResponse>> ListProductsAsync(string familyHandle)
    {
        try
        {
            var url = $"/products.json?include[]=product_family&filter[product_family_handle]={familyHandle}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var products = JsonSerializer.Deserialize<List<ProductResponse>>(content, options) ?? [];

            _logger.LogInformation($"Listed {products.Count} products for family {familyHandle}");
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to list products for family {familyHandle}");
            throw;
        }
    }

    public async Task<SubscriptionResponse> CreateSubscriptionAsync(SubscriptionCreateRequest request)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            var json = JsonSerializer.Serialize(request, options);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var subscription = JsonSerializer.Deserialize<SubscriptionResponse>(responseContent, options);

            _logger.LogInformation($"Created subscription {subscription?.Subscription.Id} for customer {subscription?.Subscription.Customer.Id}");
            return subscription ?? throw new InvalidOperationException("Failed to deserialize subscription response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription");
            throw;
        }
    }

    public async Task<List<SubscriptionResponse>> ListSubscriptionsByCustomerIdAsync(int customerId)
    {
        try
        {
            var url = $"/subscriptions.json?customer_id={customerId}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var subscriptions = JsonSerializer.Deserialize<List<SubscriptionResponse>>(content, options) ?? [];

            _logger.LogInformation($"Listed {subscriptions.Count} subscriptions for customer {customerId}");
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to list subscriptions for customer {customerId}");
            throw;
        }
    }

    public async Task<CustomerResponse?> GetOrCreateCustomerAsync(string customerReference, string firstName, string lastName, string email)
    {
        try
        {
            var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var customer = JsonSerializer.Deserialize<CustomerResponse>(content, options);
                _logger.LogInformation($"Found existing customer {customer?.Customer.Id} with reference {customerReference}");
                return customer;
            }

            var createRequest = new CustomerCreateRequest
            {
                Customer = new CustomerAttributes
                {
                    First_name = firstName,
                    Last_name = lastName,
                    Email = email,
                    Reference = customerReference
                }
            };

            var options2 = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            var json = JsonSerializer.Serialize(createRequest, options2);
            var createContent = new StringContent(json, Encoding.UTF8, "application/json");

            var createResponse = await _httpClient.PostAsync("/customers.json", createContent);
            createResponse.EnsureSuccessStatusCode();

            var createResponseContent = await createResponse.Content.ReadAsStringAsync();
            var newCustomer = JsonSerializer.Deserialize<CustomerResponse>(createResponseContent, options2);
            _logger.LogInformation($"Created customer {newCustomer?.Customer.Id} with reference {customerReference}");
            return newCustomer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get or create customer with reference {customerReference}");
            throw;
        }
    }
}
