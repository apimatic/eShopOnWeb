using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioApiClient
{
    Task<MaxioCustomerResponse?> CreateCustomerAsync(CreateCustomerRequest request);
    Task<MaxioCustomerResponse?> LookupCustomerAsync(string reference);
    Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(CreateSubscriptionRequest request);
    Task<MaxioSubscriptionResponse?> GetSubscriptionAsync(long subscriptionId);
    Task<ListSubscriptionsResponse?> ListSubscriptionsByCustomerAsync(long customerId);
    Task<ListProductsResponse?> ListProductsByFamilyAsync(string productFamilyHandle);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly string _apiKey;

    public MaxioApiClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = settings.ApiKey;

        var baseUrl = settings.GetApiBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_apiKey}:x"))}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioCustomerResponse?> CreateCustomerAsync(CreateCustomerRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/customers.json", request,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(json, options);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer in Maxio");
            throw;
        }
    }

    public async Task<MaxioCustomerResponse?> LookupCustomerAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(json, options);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer in Maxio: {Reference}", reference);
            throw;
        }
    }

    public async Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(CreateSubscriptionRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", request,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(json, options);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscriptionResponse?> GetSubscriptionAsync(long subscriptionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(json, options);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription from Maxio: {SubscriptionId}", subscriptionId);
            throw;
        }
    }

    public async Task<ListSubscriptionsResponse?> ListSubscriptionsByCustomerAsync(long customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={customerId}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

            var subscriptions = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(json, options);
            return new ListSubscriptionsResponse { Subscriptions = subscriptions ?? new() };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions from Maxio for customer: {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<ListProductsResponse?> ListProductsByFamilyAsync(string productFamilyHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products.json");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

            var products = JsonSerializer.Deserialize<List<MaxioProductResponse>>(json, options);
            var filtered = products?.Where(p => p.Product?.ProductFamily?.Handle == productFamilyHandle).ToList() ?? new();
            return new ListProductsResponse { Products = filtered };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products from Maxio");
            throw;
        }
    }
}

#region Request/Response DTOs

public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CustomerData Customer { get; set; } = new();

    public class CustomerData
    {
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public SubscriptionData Subscription { get; set; } = new();

    public class SubscriptionData
    {
        [JsonPropertyName("product_handle")]
        public string? ProductHandle { get; set; }

        [JsonPropertyName("customer_id")]
        public long? CustomerId { get; set; }

        [JsonPropertyName("customer_reference")]
        public string? CustomerReference { get; set; }

        [JsonPropertyName("customer_attributes")]
        public CustomerAttributes? CustomerAttributes { get; set; }

        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; set; } = "remittance";
    }

    public class CustomerAttributes
    {
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public class ListSubscriptionsResponse
{
    public List<MaxioSubscriptionResponse> Subscriptions { get; set; } = new();
}

public class ListProductsResponse
{
    public List<MaxioProductResponse> Products { get; set; } = new();
}

#endregion
