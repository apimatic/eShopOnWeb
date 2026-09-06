using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioClient
{
    Task<T?> GetAsync<T>(string endpoint) where T : class;
    Task<T?> PostAsync<T>(string endpoint, object requestBody) where T : class;
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        }
        else if (!string.IsNullOrEmpty(_settings.Subdomain))
        {
            _httpClient.BaseAddress = new Uri($"https://{_settings.Subdomain}.chargify.com/");
        }

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<T?> GetAsync<T>(string endpoint) where T : class
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, _jsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to call Maxio endpoint {endpoint}", ex);
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object requestBody) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseContent, _jsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to call Maxio endpoint {endpoint}", ex);
        }
    }
}

public interface IMaxioSubscriptionService
{
    Task<ListProductsResponse?> GetProductsAsync(int productFamilyId);
    Task<MaxioCustomerResponse?> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string userId);
    Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<ListSubscriptionsResponse?> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(IMaxioClient client, MaxioSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<ListProductsResponse?> GetProductsAsync(int productFamilyId)
    {
        return await _client.GetAsync<ListProductsResponse>($"product_families/{productFamilyId}/products.json");
    }

    public async Task<MaxioCustomerResponse?> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string userId)
    {
        var lookup = await _client.GetAsync<LookupCustomerResponse>($"customers/lookup.json?reference={Uri.EscapeDataString(userId)}");
        if (lookup?.Customer != null)
        {
            return new MaxioCustomerResponse { Customer = lookup.Customer };
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomerData
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Reference = userId
            }
        };

        return await _client.PostAsync<MaxioCustomerResponse>("customers.json", createRequest);
    }

    public async Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionData
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                PaymentCollectionMethod = "remittance"
            }
        };

        return await _client.PostAsync<MaxioSubscriptionResponse>("subscriptions.json", request);
    }

    public async Task<ListSubscriptionsResponse?> GetCustomerSubscriptionsAsync(int customerId)
    {
        return await _client.GetAsync<ListSubscriptionsResponse>($"customers/{customerId}/subscriptions.json");
    }
}

#region DTOs

public class ListProductsResponse
{
    public Product[]? Products { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class LookupCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public class CreateCustomerRequest
{
    public CreateCustomerData? Customer { get; set; }
}

public class CreateCustomerData
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Reference { get; set; }
}

public class CreateSubscriptionRequest
{
    public CreateSubscriptionData? Subscription { get; set; }
}

public class CreateSubscriptionData
{
    public int CustomerId { get; set; }
    public string? ProductHandle { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ListSubscriptionsResponse
{
    public MaxioSubscription[]? Subscriptions { get; set; }
}

#endregion
