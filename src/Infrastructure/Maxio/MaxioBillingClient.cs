using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP adapter for the Maxio Advanced Billing API.
///
/// Verified against the Advanced Billing API (sandbox):
/// - Basic auth: API key as username, fixed password "x".
/// - GET  /products.json                          -> [{ product: {...} }]
/// - GET  /customers/lookup.json?reference=X      -> { customer } | 404
/// - POST /customers.json { customer: {...} }     -> 201 { customer }
/// - POST /subscriptions.json                     -> 201 { subscription }
///        { subscription: { customer_id, product_handle,
///                          payment_collection_method: "invoice" } }
/// - GET  /subscriptions/{id}.json                -> { subscription } | 404
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private const string FixedBasicPassword = "x";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings, IAppLogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var apiKey = settings.Value.ApiKey ?? throw new InvalidOperationException("Maxio:ApiKey must be configured.");
        var baseUrl = settings.Value.ResolveBaseUrl();

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:{FixedBasicPassword}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var products = await GetAsync<List<ProductEnvelope>>("products.json", expectNotFound: false, cancellationToken)
            ?? new List<ProductEnvelope>();

        return products
            .Select(e => e.Product)
            .Where(p => p != null
                && p.ArchivedAt == null
                && string.Equals(p.ProductFamily?.Handle, productFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(p => new MaxioProduct(p!.Id, p.Handle!, p.Name!, p.Description, p.PriceInCents, p.IntervalUnit!, p.Interval, p.ProductFamily!.Handle!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var customer = await GetAsync<CustomerEnvelope>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", expectNotFound: true, cancellationToken);
        return customer?.Customer == null ? null : ToCustomer(customer.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var response = await SendAsync(HttpMethod.Post, "customers.json", body, expectedStatus: System.Net.HttpStatusCode.Created, cancellationToken);
        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken)
            ?? throw new MaxioBillingException((int)System.Net.HttpStatusCode.Created, new[] { "Empty response when creating customer." });

        return ToCustomer(envelope.Customer!);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int maxioCustomerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            subscription = new
            {
                customer_id = maxioCustomerId,
                product_handle = productHandle,
                // Subscribing without card capture / 3-DS: no payment profile is
                // attached and the customer is billed by remittance (invoice).
                payment_collection_method = "invoice"
            }
        };

        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, expectedStatus: System.Net.HttpStatusCode.Created, cancellationToken);
        var envelope = await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken)
            ?? throw new MaxioBillingException((int)System.Net.HttpStatusCode.Created, new[] { "Empty response when creating subscription." });

        return ToSubscription(envelope.Subscription!);
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<SubscriptionEnvelope>($"subscriptions/{subscriptionId}.json", expectNotFound: true, cancellationToken);
        return envelope?.Subscription == null ? null : ToSubscription(envelope.Subscription);
    }

    private static MaxioCustomer ToCustomer(CustomerDto dto) =>
        new(dto.Id, dto.Reference, dto.Email ?? string.Empty, dto.FirstName, dto.LastName);

    private static MaxioSubscription ToSubscription(SubscriptionDto dto) =>
        new(dto.Id, dto.State ?? string.Empty, dto.Customer?.Id ?? 0,
            dto.Product?.Handle ?? string.Empty, dto.Product?.Name ?? string.Empty,
            dto.ProductPriceInCents, dto.Currency ?? "USD", dto.NextAssessmentAt, dto.CreatedAt);

    private async Task<T?> GetAsync<T>(string path, bool expectNotFound, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(path, cancellationToken);

        if (expectNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, path, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, System.Net.HttpStatusCode expectedStatus, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(method, path) { Content = content };

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode != expectedStatus)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);
            _logger.LogWarning("Maxio API call to {Path} failed with {StatusCode}: {Errors}", path, (int)response.StatusCode, string.Join("; ", errors));
            throw new MaxioBillingException((int)response.StatusCode, errors);
        }

        return response;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ReadErrorsAsync(response, cancellationToken);
        _logger.LogWarning("Maxio API call to {Path} failed with {StatusCode}: {Errors}", path, (int)response.StatusCode, string.Join("; ", errors));
        throw new MaxioBillingException((int)response.StatusCode, errors);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
            {
                return errorsElement.EnumerateArray().Select(e => e.GetString() ?? e.ToString()).ToList();
            }
            return new[] { body };
        }
        catch
        {
            return new[] { $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}" };
        }
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    // --- wire DTOs (Advanced Billing JSON, snake_case) ---

    private sealed class ProductEnvelope
    {
        [JsonPropertyName("product")]
        public ProductDto? Product { get; set; }
    }

    private sealed class ProductDto
    {
        public int Id { get; set; }
        public string? Handle { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int PriceInCents { get; set; }
        public string? IntervalUnit { get; set; }
        public int Interval { get; set; }
        public string? ArchivedAt { get; set; }
        public ProductFamilyDto? ProductFamily { get; set; }
    }

    private sealed class ProductFamilyDto
    {
        public int Id { get; set; }
        public string? Handle { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public CustomerDto? Customer { get; set; }
    }

    private sealed class CustomerDto
    {
        public int Id { get; set; }
        public string? Reference { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public SubscriptionDto? Subscription { get; set; }
    }

    private sealed class SubscriptionDto
    {
        public int Id { get; set; }
        public string? State { get; set; }
        public CustomerDto? Customer { get; set; }
        public ProductDto? Product { get; set; }
        public int ProductPriceInCents { get; set; }
        public string? Currency { get; set; }
        public string? NextAssessmentAt { get; set; }
        public string? CreatedAt { get; set; }
    }
}
