using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        IConfiguration configuration,
        ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = _options.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var environment = configuration["MAXIO_ENVIRONMENT"]?.Trim().ToUpperInvariant();
            baseUrl = environment switch
            {
                "EU" => $"https://{_options.Subdomain}.ebilling.maxio.com",
                "US" or null or "" => $"https://{_options.Subdomain}.chargify.com",
                _ => throw new InvalidOperationException("MAXIO_ENVIRONMENT must be US or EU when Maxio:BaseUrl is not set.")
            };
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URL.");
        }

        _httpClient.BaseAddress = uri;
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var products = new List<MaxioProduct>();
        const int pageSize = 200;

        for (var page = 1; ; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?page={page}&per_page={pageSize}";
            var pageProducts = await SendAsync<MaxioProductResponse[]>(HttpMethod.Get, path, null, cancellationToken);
            if (pageProducts is null || pageProducts.Length == 0)
            {
                break;
            }

            products.AddRange(pageProducts.Where(item => item.Product is not null).Select(item => item.Product!));
            if (pageProducts.Length < pageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try
        {
            var response = await SendAsync<MaxioCustomerResponse>(
                HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                null,
                cancellationToken);
            return response?.Customer;
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            },
            cancellationToken);

        return response?.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await SendAsync<MaxioSubscriptionResponse[]>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return response is null
            ? Array.Empty<MaxioSubscription>()
            : response.Where(item => item.Subscription is not null).Select(item => item.Subscription!).ToArray();
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try
        {
            var response = await SendAsync<MaxioSubscriptionResponse>(
                HttpMethod.Get,
                $"subscriptions/{subscriptionId}.json",
                null,
                cancellationToken);
            return response?.Subscription;
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle,
        int customerId,
        string reference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = reference,
                    PaymentCollectionMethod = "remittance"
                }
            },
            cancellationToken);

        return response?.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription response.");
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Keep provider response bodies out of logs and client responses: they may contain
            // customer data. The status is enough for retry and support diagnostics.
            _logger.LogWarning("Maxio request {Method} {Path} failed with HTTP {StatusCode}.", method, path, (int)response.StatusCode);
            throw new MaxioApiException(response.StatusCode, "Maxio billing provider request failed.");
        }

        if (response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.");
        }
    }
}

public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("trial_interval")] public int? TrialInterval { get; set; }
    [JsonPropertyName("trial_interval_unit")] public string? TrialIntervalUnit { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("handle")] public string? Handle { get; set; }
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

public sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")] public MaxioCreateCustomer Customer { get; set; } = new();
}

public sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("product_price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_id")] public int CustomerId { get; set; }
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = string.Empty;
}
