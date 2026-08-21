using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private const int PageSize = 200;
    private const int MaximumPages = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var family = $"handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}";

        for (var page = 1; page <= MaximumPages; page++)
        {
            var path = $"product_families/{family}/products.json?page={page}&per_page={PageSize}";
            var response = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
            products.AddRange(response.Select(item => Map(item.Product)));

            if (response.Count < PageSize)
            {
                return products;
            }
        }

        throw new MaxioApiException($"Maxio returned more than {MaximumPages * PageSize} products for the configured family.");
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendOptionalAsync<CustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
        return response == null ? null : Map(response.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer request, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = request.FirstName,
                last_name = request.LastName,
                email = request.Email,
                reference = request.Reference
            },
            uniqueness_token = request.UniquenessToken
        };
        var response = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        return Map(response.Customer);
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendOptionalAsync<SubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken);
        return response == null ? null : Map(response.Subscription);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription request, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = request.ProductHandle,
                customer_id = request.CustomerId,
                reference = request.Reference,
                payment_collection_method = request.PaymentCollectionMethod
            },
            uniqueness_token = request.UniquenessToken
        };
        var response = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return Map(response.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return response.Select(item => Map(item.Subscription)).ToList();
    }

    private async Task<T?> SendOptionalAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        where T : class
    {
        using var request = CreateRequest(method, path, body);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioApiException("The Maxio API request could not be completed.", null, exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return await ReadResponseAsync<T>(response, path, cancellationToken);
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioApiException("The Maxio API request could not be completed.", null, exception);
        }

        using (response)
        {
            return await ReadResponseAsync<T>(response, path, cancellationToken);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        var baseUrl = _options.GetBaseUrl();
        var request = new HttpRequestMessage(method, $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var providerError = await ReadProviderErrorAsync(response, cancellationToken);
            _logger.LogWarning(
                "Maxio request to {Path} failed with HTTP {StatusCode}: {ProviderError}",
                path.Split('?')[0],
                (int)response.StatusCode,
                providerError);
            throw new MaxioApiException($"Maxio rejected the billing request: {providerError}", response.StatusCode);
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new JsonException("The response body was empty.");
        }
        catch (JsonException exception)
        {
            throw new MaxioApiException("Maxio returned an invalid response.", response.StatusCode, exception);
        }
    }

    private static async Task<string> ReadProviderErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return response.ReasonPhrase ?? "No error details were returned.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var value = errors.ToString();
                return value.Length <= 1000 ? value : value[..1000];
            }
        }
        catch (JsonException)
        {
            // Fall through to a bounded, single-line representation.
        }

        var singleLine = body.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= 1000 ? singleLine : singleLine[..1000];
    }

    private static MaxioProduct Map(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Handle = product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequireCreditCard = product.RequireCreditCard,
        ArchivedAt = product.ArchivedAt,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static MaxioCustomer Map(Customer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference ?? string.Empty
    };

    private static MaxioSubscription Map(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductPriceInCents = subscription.ProductPriceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        Reference = subscription.Reference,
        Product = subscription.Product == null ? null : Map(subscription.Product)
    };

    private sealed class ProductEnvelope
    {
        [JsonPropertyName("product")]
        public Product Product { get; init; } = new();
    }

    private sealed class CustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public Customer Customer { get; init; } = new();
    }

    private sealed class SubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public Subscription Subscription { get; init; } = new();
    }

    private sealed class Product
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("handle")]
        public string? Handle { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("price_in_cents")]
        public long PriceInCents { get; init; }

        [JsonPropertyName("interval")]
        public int Interval { get; init; }

        [JsonPropertyName("interval_unit")]
        public string IntervalUnit { get; init; } = string.Empty;

        [JsonPropertyName("require_credit_card")]
        public bool RequireCreditCard { get; init; }

        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; init; }

        [JsonPropertyName("product_family")]
        public ProductFamily? ProductFamily { get; init; }
    }

    private sealed class ProductFamily
    {
        [JsonPropertyName("handle")]
        public string Handle { get; init; } = string.Empty;
    }

    private sealed class Customer
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("reference")]
        public string? Reference { get; init; }
    }

    private sealed class Subscription
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;

        [JsonPropertyName("product_price_in_cents")]
        public long ProductPriceInCents { get; init; }

        [JsonPropertyName("current_period_ends_at")]
        public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

        [JsonPropertyName("next_assessment_at")]
        public DateTimeOffset? NextAssessmentAt { get; init; }

        [JsonPropertyName("reference")]
        public string? Reference { get; init; }

        [JsonPropertyName("product")]
        public Product? Product { get; init; }
    }
}
