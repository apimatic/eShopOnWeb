using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioApiClient(
        HttpClient httpClient,
        IOptions<MaxioConfiguration> config,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
    }

    public void Initialize()
    {
        var baseUrl = !string.IsNullOrEmpty(_config.BaseUrl)
            ? _config.BaseUrl.TrimEnd('/')
            : $"https://{_config.Subdomain}.chargify.com";

        _httpClient.BaseAddress = new Uri(baseUrl + "/");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// List all products. GET /products.json returns an array of Product-Response.
    /// </summary>
    public async Task<List<ProductResponse>?> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("products.json", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to list products. Status: {Status}, Body: {Body}", response.StatusCode, content);
            return null;
        }

        return JsonSerializer.Deserialize<List<ProductResponse>>(content, JsonOptions);
    }

    /// <summary>
    /// Find customer by reference. GET /customers/lookup.json?reference={reference}
    /// </summary>
    public async Task<CustomerResponse?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to find customer. Status: {Status}, Body: {Body}", response.StatusCode, content);
            return null;
        }

        return JsonSerializer.Deserialize<CustomerResponse>(content, JsonOptions);
    }

    /// <summary>
    /// Create a customer. POST /customers.json
    /// </summary>
    public async Task<CustomerResponse?> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { customer = request }, JsonOptions);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("customers.json", content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create customer. Status: {Status}, Body: {Body}", response.StatusCode, responseContent);
            return null;
        }

        return JsonSerializer.Deserialize<CustomerResponse>(responseContent, JsonOptions);
    }

    /// <summary>
    /// Idempotent: find by reference first, create if not found.
    /// </summary>
    public async Task<CustomerResponse?> EnsureCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing?.Customer != null)
        {
            _logger.LogInformation("Found existing Maxio customer {Id} for reference {Reference}", existing.Customer.Id, reference);
            return existing;
        }

        _logger.LogInformation("Creating new Maxio customer for reference {Reference}", reference);
        return await CreateCustomerAsync(new CreateCustomerRequest
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = reference
        }, cancellationToken);
    }

    /// <summary>
    /// Create a subscription. POST /subscriptions.json
    /// </summary>
    public async Task<SubscriptionResponse?> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { subscription = request }, JsonOptions);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("subscriptions.json", content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create subscription. Status: {Status}, Body: {Body}", response.StatusCode, responseContent);
            return null;
        }

        return JsonSerializer.Deserialize<SubscriptionResponse>(responseContent, JsonOptions);
    }

    /// <summary>
    /// Find subscription by reference. GET /subscriptions/lookup.json?reference={reference}
    /// </summary>
    public async Task<SubscriptionResponse?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to find subscription. Status: {Status}, Body: {Body}", response.StatusCode, content);
            return null;
        }

        return JsonSerializer.Deserialize<SubscriptionResponse>(content, JsonOptions);
    }

    /// <summary>
    /// List all subscriptions. GET /subscriptions.json returns an array of Subscription-Response.
    /// </summary>
    public async Task<List<SubscriptionResponse>?> ListSubscriptionsAsync(string? state = null, int? page = null, int? perPage = null, CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrEmpty(state)) queryParts.Add($"state={Uri.EscapeDataString(state)}");
        if (page.HasValue) queryParts.Add($"page={page.Value}");
        if (perPage.HasValue) queryParts.Add($"per_page={perPage.Value}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        var response = await _httpClient.GetAsync($"subscriptions.json{query}", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to list subscriptions. Status: {Status}, Body: {Body}", response.StatusCode, content);
            return null;
        }

        return JsonSerializer.Deserialize<List<SubscriptionResponse>>(content, JsonOptions);
    }
}

// ── Request DTOs ──

public class CreateCustomerRequest
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class CreateMaxioSubscriptionRequest
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

// ── Response DTOs ──

public class ProductResponse
{
    [JsonPropertyName("product")]
    public ProductInfo? Product { get; set; }
}

public class ProductInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamilyInfo? ProductFamily { get; set; }

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }
}

public class ProductFamilyInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class CustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public string? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public string? CanceledAt { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product")]
    public ProductInfo? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}
