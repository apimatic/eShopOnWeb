using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = _settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        _logger.LogInformation("Looking up Maxio customer by reference: {Reference}", reference);
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("No Maxio customer found with reference: {Reference}", reference);
            return null;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var wrapper = JsonSerializer.Deserialize<MaxioCustomerWrapper>(json, s_jsonOptions);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Maxio customer: {Email}", request.Email);
        var payload = new MaxioCustomerWrapper { Customer = new MaxioCustomer
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Reference = request.Reference,
            Organization = request.Organization
        }};

        var content = new StringContent(JsonSerializer.Serialize(payload, s_jsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("customers.json", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var wrapper = JsonSerializer.Deserialize<MaxioCustomerWrapper>(json, s_jsonOptions);
        return wrapper!.Customer;
    }

    public async Task<List<MaxioProduct>> ListProductsAsync(string? productFamilyHandle = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Listing Maxio products for family: {FamilyHandle}", productFamilyHandle ?? "(all)");
        var url = "products.json?per_page=200";
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var document = JsonDocument.Parse(json);
        var products = new List<MaxioProduct>();

        // Maxio returns a flat array: [{"product": {...}}, ...]
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var productElement))
                {
                    var product = JsonSerializer.Deserialize<MaxioProduct>(productElement.GetRawText(), s_jsonOptions);
                    if (product != null)
                    {
                        if (!string.IsNullOrEmpty(productFamilyHandle) &&
                            product.ProductFamily?.Handle != productFamilyHandle)
                        {
                            continue;
                        }
                        products.Add(product);
                    }
                }
            }
        }

        return products;
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting Maxio subscription: {SubscriptionId}", subscriptionId);
        var response = await _httpClient.GetAsync($"subscriptions/{subscriptionId}.json", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var wrapper = JsonSerializer.Deserialize<MaxioSubscriptionWrapper>(json, s_jsonOptions);
        return wrapper?.Subscription;
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        _logger.LogInformation("Listing subscriptions for Maxio customer: {CustomerId}", customerId);
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json?per_page=200", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var document = JsonDocument.Parse(json);
        var subscriptions = new List<MaxioSubscription>();

        // Maxio returns a flat array: [{"subscription": {...}}, ...]
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("subscription", out var subElement))
                {
                    var sub = JsonSerializer.Deserialize<MaxioSubscription>(subElement.GetRawText(), s_jsonOptions);
                    if (sub != null)
                        subscriptions.Add(sub);
                }
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Maxio subscription for product handle: {ProductHandle}", request.Subscription.ProductHandle);
        var createPayload = new
        {
            subscription = new
            {
                product_handle = request.Subscription.ProductHandle,
                product_id = request.Subscription.ProductId,
                customer_reference = request.Subscription.CustomerReference,
                customer_id = request.Subscription.CustomerId,
                customer_attributes = request.Subscription.CustomerAttributes != null ? new
                {
                    first_name = request.Subscription.CustomerAttributes.FirstName,
                    last_name = request.Subscription.CustomerAttributes.LastName,
                    email = request.Subscription.CustomerAttributes.Email,
                    reference = request.Subscription.CustomerAttributes.Reference
                } : null,
                payment_collection_method = request.Subscription.PaymentCollectionMethod ?? "invoice"
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(createPayload, s_jsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("subscriptions.json", content, ct);

        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio subscription creation failed with status {StatusCode}: {Response}",
                response.StatusCode, responseJson);
            throw new InvalidOperationException($"Maxio subscription creation failed: {response.StatusCode} - {responseJson}");
        }

        var wrapper = JsonSerializer.Deserialize<MaxioSubscriptionWrapper>(responseJson, s_jsonOptions);
        return wrapper!.Subscription;
    }
}
