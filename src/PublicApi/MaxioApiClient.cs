using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioApiClient
{
    Task<List<MaxioProduct>> ListProductsAsync(string productFamilyHandle);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string? uniquenessToken = null);
    Task<List<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioApiClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = _settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<List<MaxioProduct>> ListProductsAsync(string productFamilyHandle)
    {
        var url = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        _logger.LogInformation("Listing products from {Url}", url);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<List<MaxioProductItem>>(json, JsonOptions);
        return items?.ConvertAll(i => i.Product) ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogInformation("Looking up customer by reference {Reference}", reference);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<MaxioCustomerWrapper>(json, JsonOptions);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var url = "customers.json";
        _logger.LogInformation("Creating customer with reference {Reference}", reference);

        var body = new
        {
            customer = new
            {
                reference,
                email,
                first_name = firstName,
                last_name = lastName
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<MaxioCustomerWrapper>(json, JsonOptions);
        return wrapper!.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string? uniquenessToken = null)
    {
        var url = "subscriptions.json";
        _logger.LogInformation("Creating subscription for product {ProductHandle}, customer {CustomerId}", productHandle, customerId);

        var subscriptionBody = new Dictionary<string, object>
        {
            ["product_handle"] = productHandle,
            ["customer_id"] = customerId,
            ["payment_collection_method"] = "remittance"
        };

        var body = new Dictionary<string, object>
        {
            ["subscription"] = subscriptionBody
        };

        if (!string.IsNullOrEmpty(uniquenessToken))
        {
            body["uniqueness_token"] = uniquenessToken;
        }

        var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<MaxioSubscriptionWrapper>(json, JsonOptions);
        return wrapper!.Subscription;
    }

    public async Task<List<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        _logger.LogInformation("Listing subscriptions for customer {CustomerId}", customerId);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<List<MaxioSubscriptionItem>>(json, JsonOptions);
        return items?.ConvertAll(i => i.Subscription) ?? new List<MaxioSubscription>();
    }
}

// JSON deserialization models matching Maxio API snake_case responses

public class MaxioProductListWrapper
{
    [JsonPropertyName("items")]
    public List<MaxioProductItem> Items { get; set; } = new();
}

public class MaxioProductItem
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("product_family")]
    public MaxioProductFamily ProductFamily { get; set; } = new();

    [JsonPropertyName("archived_at")]
    public DateTime? ArchivedAt { get; set; }
}

public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

public class MaxioCustomerWrapper
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

public class MaxioSubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public class MaxioSubscriptionListWrapper
{
    [JsonPropertyName("items")]
    public List<MaxioSubscriptionItem> Items { get; set; } = new();
}

public class MaxioSubscriptionItem
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
