using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription?> GetSubscriptionAsync(long id, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string subscriptionReference, long customerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode)
        : base($"Maxio Billing API returned {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class MaxioProduct
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; init; } = string.Empty;
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; init; }
    public bool Taxable { get; init; }
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed class MaxioProductFamily
{
    public string Handle { get; init; } = string.Empty;
}

public sealed class MaxioCustomer
{
    public long Id { get; init; }
    public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioSubscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string Reference { get; init; } = string.Empty;
    public MaxioCustomer? Customer { get; init; }
    public MaxioProduct? Product { get; init; }
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("The Maxio configuration requires ApiKey, Subdomain, and ProductFamilyHandle.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com/"
            : _options.BaseUrl!.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/";

        _httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?page={page}&per_page=200&include_archived=false";
            var pageItems = await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken);
            foreach (var item in pageItems)
            {
                if (item.Product is not null && !string.IsNullOrWhiteSpace(item.Product.Handle))
                {
                    products.Add(item.Product);
                }
            }

            if (pageItems.Count < 200)
            {
                return products;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        (await GetOptionalAsync<MaxioCustomerEnvelope>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken))?.Customer;

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var body = new MaxioCustomerCreateEnvelope
        {
            Customer = new MaxioCustomerCreate
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            }
        };

        var response = await PostAsync<MaxioCustomerEnvelope>("customers.json", body, cancellationToken);
        return response.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        GetOptionalAsync<MaxioSubscriptionEnvelope>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken)
            .ContinueWith(task => task.Result?.Subscription, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public async Task<MaxioSubscription?> GetSubscriptionAsync(long id, CancellationToken cancellationToken) =>
        (await GetOptionalAsync<MaxioSubscriptionEnvelope>($"subscriptions/{id}.json", cancellationToken))?.Subscription;

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string subscriptionReference, long customerId, CancellationToken cancellationToken)
    {
        var body = new MaxioSubscriptionCreateEnvelope
        {
            Subscription = new MaxioSubscriptionCreate
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", body, cancellationToken);
        return response.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var items = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
        foreach (var item in items)
        {
            if (item.Subscription is not null)
            {
                subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio Billing API request failed with status {StatusCode}", response.StatusCode);
            throw new MaxioApiException(response.StatusCode);
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
    }

    private sealed class MaxioProductEnvelope
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; init; }
    }

    private sealed class MaxioCustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; init; }
    }

    private sealed class MaxioCustomerCreateEnvelope
    {
        [JsonPropertyName("customer")]
        public MaxioCustomerCreate Customer { get; init; } = new();
    }

    private sealed class MaxioCustomerCreate
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; init; } = string.Empty;
        [JsonPropertyName("last_name")]
        public string LastName { get; init; } = string.Empty;
        [JsonPropertyName("email")]
        public string Email { get; init; } = string.Empty;
        [JsonPropertyName("reference")]
        public string Reference { get; init; } = string.Empty;
    }

    private sealed class MaxioSubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; init; }
    }

    private sealed class MaxioSubscriptionCreateEnvelope
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscriptionCreate Subscription { get; init; } = new();
    }

    private sealed class MaxioSubscriptionCreate
    {
        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; init; } = string.Empty;
        [JsonPropertyName("customer_id")]
        public long CustomerId { get; init; }
        [JsonPropertyName("reference")]
        public string Reference { get; init; } = string.Empty;
        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; init; } = "remittance";
    }
}
