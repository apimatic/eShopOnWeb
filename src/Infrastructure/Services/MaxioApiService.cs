using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioApiService
{
    Task<MaxioProductDto?> GetProductByHandleAsync(string handle);
    Task<MaxioCustomerDto?> LookupCustomerByReferenceAsync(string reference);
    Task<MaxioCustomerDto?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email);
    Task<MaxioSubscriptionDto?> CreateSubscriptionAsync(int customerId, int productId, string productHandle);
    Task<MaxioSubscriptionDto?> ReadSubscriptionAsync(int subscriptionId);
    Task<MaxioSubscriptionListDto?> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioApiService : IMaxioApiService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioApiService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioApiService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    }

    public async Task<MaxioProductDto?> GetProductByHandleAsync(string handle)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/products/handle/{handle}.json";
            var response = await GetJsonAsync<MaxioProductResponseDto>(url);
            return response?.Product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product by handle: {Handle}", handle);
            return null;
        }
    }

    public async Task<MaxioCustomerDto?> LookupCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await GetJsonAsync<MaxioCustomerResponseDto>(url);
            return response?.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer by reference: {Reference}", reference);
            return null;
        }
    }

    public async Task<MaxioCustomerDto?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email)
    {
        var existing = await LookupCustomerByReferenceAsync(userId);
        if (existing != null)
            return existing;

        return await CreateCustomerAsync(userId, firstName, lastName, email);
    }

    public async Task<MaxioSubscriptionDto?> CreateSubscriptionAsync(int customerId, int productId, string productHandle)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/subscriptions.json";
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await PostJsonAsync<MaxioSubscriptionResponseDto>(url, content);
            return response?.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer: {CustomerId}, product: {ProductId}", customerId, productId);
            return null;
        }
    }

    public async Task<MaxioSubscriptionDto?> ReadSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/subscriptions/{subscriptionId}.json";
            var response = await GetJsonAsync<MaxioSubscriptionResponseDto>(url);
            return response?.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading subscription: {SubscriptionId}", subscriptionId);
            return null;
        }
    }

    public async Task<MaxioSubscriptionListDto?> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/{customerId}/subscriptions.json";
            var response = await GetJsonAsync<MaxioSubscriptionListDto>(url);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for customer: {CustomerId}", customerId);
            return null;
        }
    }

    private async Task<MaxioCustomerDto?> CreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers.json";
            var payload = new
            {
                customer = new
                {
                    reference,
                    first_name = firstName,
                    last_name = lastName,
                    email
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await PostJsonAsync<MaxioCustomerResponseDto>(url, content);
            return response?.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer with reference: {Reference}", reference);
            return null;
        }
    }

    private async Task<T?> GetJsonAsync<T>(string url) where T : class
    {
        AddAuthHeader();
        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private async Task<T?> PostJsonAsync<T>(string url, StringContent content) where T : class
    {
        AddAuthHeader();
        using var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private void AddAuthHeader()
    {
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
    }
}

public class MaxioProductResponseDto
{
    public MaxioProductDto? Product { get; set; }
}

public class MaxioProductDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
}

public class MaxioCustomerResponseDto
{
    public MaxioCustomerDto? Customer { get; set; }
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MaxioSubscriptionResponseDto
{
    public MaxioSubscriptionDto? Subscription { get; set; }
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public int? CustomerId { get; set; }
    public int? ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public long? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public long? BalanceInCents { get; set; }
    public long? MrrInCents { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MaxioSubscriptionListDto
{
    public List<MaxioSubscriptionDto> Subscriptions { get; set; } = new List<MaxioSubscriptionDto>();
}
