using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        var baseUrl = string.IsNullOrEmpty(_settings.BaseUrl)
            ? $"https://{_settings.Subdomain}.chargify.com"
            : _settings.BaseUrl.TrimEnd('/');

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<List<ProductDto>?> ListProductsAsync(string productFamilyHandle)
    {
        var response = await _httpClient.GetAsync($"product_families/handle:{productFamilyHandle}/products.json");
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<ProductItemWrapper>>(JsonOptions);
        return items?.ConvertAll(i => i.Product) ?? new List<ProductDto>();
    }

    public async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOptions);
        return wrapper?.Customer;
    }

    public async Task<CustomerDto> CreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };
        var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions);
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var existing = await FindCustomerByReferenceAsync(reference);
            if (existing != null) return existing;
        }
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOptions);
        return wrapper!.Customer!;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                payment_collection_method = "invoice"
            }
        };
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<SubscriptionWrapper>(JsonOptions);
        return wrapper!.Subscription!;
    }

    public async Task<List<SubscriptionDto>?> ListCustomerSubscriptionsAsync(int customerId)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json");
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<SubscriptionItemWrapper>>(JsonOptions);
        return items?.ConvertAll(i => i.Subscription) ?? new List<SubscriptionDto>();
    }
}

// Maxio API response DTOs

public class ProductListWrapper
{
    [JsonPropertyName("items")]
    public List<ProductItemWrapper>? Items { get; set; }
}

public class ProductItemWrapper
{
    [JsonPropertyName("product")]
    public ProductDto Product { get; set; } = null!;
}

public class ProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }
}

public class CustomerWrapper
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

public class CustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class SubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionItemWrapper
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto Subscription { get; set; } = null!;
}

public class SubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}
