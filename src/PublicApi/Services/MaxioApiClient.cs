using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;

    public MaxioApiClient(HttpClient httpClient, MaxioConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
        ConfigureClient();
    }

    private void ConfigureClient()
    {
        if (!string.IsNullOrEmpty(_config.Subdomain) || !string.IsNullOrEmpty(_config.BaseUrl))
        {
            var baseUrl = _config.BaseUrl ?? $"https://{_config.Subdomain}.chargify.com";
            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");
            }
        }
    }

    public async Task<MaxioProductsResponse?> GetProductsByFamilyHandleAsync(string familyHandle)
    {
        try
        {
            if (_httpClient.BaseAddress == null)
            {
                throw new InvalidOperationException("Maxio configuration is not set");
            }

            var response = await _httpClient.GetAsync($"/product_families/lookup.json?handle={familyHandle}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MaxioProductsResponse>(content, JsonSerializerOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to fetch products from Maxio: {ex.Message}", ex);
        }
    }

    public async Task<MaxioCustomerResponse?> CreateCustomerAsync(CreateMaxioCustomerRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonSerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/customers.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Maxio error: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MaxioCustomerResponse>(responseContent, JsonSerializerOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create customer in Maxio: {ex.Message}", ex);
        }
    }

    public async Task<MaxioCustomerResponse?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MaxioCustomerResponse>(content, JsonSerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonSerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Maxio error: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseContent, JsonSerializerOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create subscription in Maxio: {ex.Message}", ex);
        }
    }

    public async Task<MaxioCustomerSubscriptionsResponse?> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MaxioCustomerSubscriptionsResponse>(content, JsonSerializerOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to fetch customer subscriptions from Maxio: {ex.Message}", ex);
        }
    }

    private JsonSerializerOptions JsonSerializerOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}

#region DTOs

public class MaxioProductsResponse
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public List<MaxioProduct> Products { get; set; } = new();
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CreateMaxioCustomerRequest
{
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class MaxioCustomerResponse
{
    public MaxioCustomerData? Customer { get; set; }
}

public class MaxioCustomerData
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class CreateMaxioSubscriptionRequest
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public class MaxioSubscription
{
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? ProductHandle { get; set; }
    public string? PaymentCollectionMethod { get; set; } = "remittance";
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscriptionData? Subscription { get; set; }
}

public class MaxioSubscriptionData
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public MaxioProductData? Product { get; set; }
    public MaxioCustomerData? Customer { get; set; }
}

public class MaxioProductData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class MaxioCustomerSubscriptionsResponse : List<MaxioSubscriptionResponse>
{
}

#endregion
