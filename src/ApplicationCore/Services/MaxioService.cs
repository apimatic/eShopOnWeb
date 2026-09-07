using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configurations;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioService
{
    Task<MaxioSubscriptionPlanDto[]> ListSubscriptionPlansAsync();
    Task<MaxioCustomerDto> EnsureCustomerAsync(string userId, string email, string firstName = "", string lastName = "");
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle);
    Task<MaxioSubscriptionDto[]> ListCustomerSubscriptionsAsync(long customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, MaxioConfiguration config, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        try
        {
            var subdomain = string.IsNullOrEmpty(_config.Subdomain) ? "cp-exp-4" : _config.Subdomain;
            var baseUrl = _config.BaseUrl ?? $"https://{subdomain}.chargify.com";

            if (!string.IsNullOrEmpty(baseUrl))
            {
                _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
            }

            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
            }

            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error configuring Maxio HTTP client");
        }
    }

    public async Task<MaxioSubscriptionPlanDto[]> ListSubscriptionPlansAsync()
    {
        try
        {
            if (_httpClient.BaseAddress == null)
            {
                throw new InvalidOperationException("Maxio BaseAddress is not configured. Check MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN environment variables.");
            }

            var response = await _httpClient.GetAsync("/products.json");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioProductsResponse>(content, options);
            return result?.Products ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscription plans from Maxio");
            throw;
        }
    }

    public async Task<MaxioCustomerDto> EnsureCustomerAsync(string userId, string email, string firstName = "", string lastName = "")
    {
        try
        {
            var listResponse = await _httpClient.GetAsync($"/customers.json?reference={Uri.EscapeDataString(userId)}");
            if (listResponse.IsSuccessStatusCode)
            {
                var content = await listResponse.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<MaxioCustomersResponse>(content, options);
                if (result?.Customers?.Length > 0)
                {
                    return result.Customers[0];
                }
            }

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

            var json = JsonSerializer.Serialize(createRequest);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            var createResponse = await _httpClient.PostAsync("/customers.json", httpContent);
            createResponse.EnsureSuccessStatusCode();

            var createContent = await createResponse.Content.ReadAsStringAsync();
            var options2 = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var createResult = JsonSerializer.Deserialize<MaxioCustomerResponse>(createContent, options2);
            return createResult?.Customer ?? throw new InvalidOperationException("Failed to create customer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring customer in Maxio for user {UserId}", userId);
            throw;
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle)
    {
        try
        {
            var createRequest = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_method_nonce = "nonce_no_payment_method"
                }
            };

            var json = JsonSerializer.Serialize(createRequest);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", httpContent);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(content, options);
            return result?.Subscription ?? throw new InvalidOperationException("Failed to create subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio for customer {CustomerId} with product {ProductHandle}", customerId, productHandle);
            throw;
        }
    }

    public async Task<MaxioSubscriptionDto[]> ListCustomerSubscriptionsAsync(long customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioSubscriptionsResponse>(content, options);
            return result?.Subscriptions ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions from Maxio for customer {CustomerId}", customerId);
            throw;
        }
    }
}

public class MaxioSubscriptionPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal PriceInCents { get; set; }
    public int IntervalUnit { get; set; }
    public int Interval { get; set; }
    public string? IntervalDescription => Interval == 1 ? (IntervalUnit == 1 ? "month" : "year") : null;
}

public class MaxioProductsResponse
{
    public MaxioSubscriptionPlanDto[]? Products { get; set; }
}

public class MaxioCustomerDto
{
    public long Id { get; set; }
    public string Email { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCustomersResponse
{
    public MaxioCustomerDto[]? Customers { get; set; }
}

public class MaxioCustomerResponse
{
    public MaxioCustomerDto? Customer { get; set; }
}

public class MaxioSubscriptionDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string State { get; set; } = null!;
    public decimal CurrentPrice { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public int BillingPeriodLength { get; set; }
    public string BillingPeriodUnit { get; set; } = null!;
    public long? ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscriptionDto? Subscription { get; set; }
}

public class MaxioSubscriptionsResponse
{
    public MaxioSubscriptionDto[]? Subscriptions { get; set; }
}
