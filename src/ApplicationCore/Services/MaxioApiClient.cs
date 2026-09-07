using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioApiClient
{
    Task<MaxioProductResponse[]> GetProductsByFamilyHandleAsync(string familyHandle, CancellationToken cancellationToken = default);
    Task<MaxioCustomerResponse> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);
    Task<MaxioCustomerResponse> GetOrCreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);
    Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);
    Task<MaxioSubscriptionResponse[]> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioConfiguration> config, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;

        // Only configure if credentials are provided
        if (!string.IsNullOrEmpty(_config.ApiKey) && !string.IsNullOrEmpty(_config.Subdomain))
        {
            var baseUrl = _config.BaseUrl ?? $"https://{_config.Subdomain}.maxio.com/";
            _httpClient.BaseAddress = new Uri(baseUrl);

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else
        {
            _logger.LogWarning("Maxio credentials not configured. Set Maxio:ApiKey and Maxio:Subdomain in user secrets or environment variables.");
        }
    }

    public async Task<MaxioProductResponse[]> GetProductsByFamilyHandleAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(familyHandle, nameof(familyHandle));

        var request = new HttpRequestMessage(HttpMethod.Get, $"products.json?per_page=200");
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<MaxioProductListResponse>(json, options);

        var products = result?.Items?.Select(i => i.Product).Where(p => p.ProductFamily?.Handle == familyHandle).ToArray() ?? [];
        return products;
    }

    public async Task<MaxioCustomerResponse> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(firstName, nameof(firstName));
        Guard.Against.NullOrEmpty(lastName, nameof(lastName));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(reference, nameof(reference));

        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("customers.json", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<MaxioCustomerResponseWrapper>(responseJson, options);

        Guard.Against.Null(result?.Customer, nameof(result.Customer));
        return result.Customer!;
    }

    public async Task<MaxioCustomerResponse> GetOrCreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));

        try
        {
            var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<MaxioCustomerResponseWrapper>(json, options);
                if (result?.Customer != null)
                {
                    _logger.LogInformation("Found existing Maxio customer for reference {Reference}", reference);
                    return result.Customer;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to lookup customer, will attempt creation");
        }

        _logger.LogInformation("Creating new Maxio customer for reference {Reference}", reference);
        return await CreateCustomerAsync(firstName, lastName, email, reference, cancellationToken);
    }

    public async Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(customerId, nameof(customerId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var payload = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("subscriptions.json", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<MaxioSubscriptionResponseWrapper>(responseJson, options);

        Guard.Against.Null(result?.Subscription, nameof(result.Subscription));
        return result.Subscription!;
    }

    public async Task<MaxioSubscriptionResponse[]> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(customerId, nameof(customerId));

        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<MaxioSubscriptionListResponse>(json, options);

        return result?.Items?.Select(i => i.Subscription).ToArray() ?? [];
    }
}

// DTO classes for Maxio API responses
public class MaxioProductResponse
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
    public string IntervalUnit { get; set; } = "month";

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class MaxioProductListResponse
{
    [JsonPropertyName("items")]
    public MaxioProductItem[]? Items { get; set; }
}

public class MaxioProductItem
{
    [JsonPropertyName("product")]
    public MaxioProductResponse Product { get; set; } = new();
}

public class MaxioCustomerResponse
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

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public class MaxioCustomerResponseWrapper
{
    [JsonPropertyName("customer")]
    public MaxioCustomerResponse? Customer { get; set; }
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product")]
    public MaxioProductResponse? Product { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public class MaxioSubscriptionResponseWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionResponse? Subscription { get; set; }
}

public class MaxioSubscriptionListResponse
{
    [JsonPropertyName("items")]
    public MaxioSubscriptionItem[]? Items { get; set; }
}

public class MaxioSubscriptionItem
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionResponse Subscription { get; set; } = new();
}
