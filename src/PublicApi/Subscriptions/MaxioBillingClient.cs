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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken);
    Task<MaxioSubscription> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private const int PageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();

        var products = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var path = $"/product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?page={page}&per_page={PageSize}";
            var pageProducts = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
            foreach (var envelope in pageProducts)
            {
                if (envelope.Product is not null && envelope.Product.ArchivedAt is null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (pageProducts.Count < PageSize)
            {
                return products;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var request = CreateRequest(HttpMethod.Get, path, null);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadResponseAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var envelope = new MaxioCreateCustomerEnvelope { Customer = customer };
        return (await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "/customers.json", envelope, cancellationToken)).Customer
            ?? throw new MaxioApiException("Maxio returned an empty customer response.", HttpStatusCode.BadGateway);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var subscriptions = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"/customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        var result = new List<MaxioSubscription>();
        foreach (var envelope in subscriptions)
        {
            if (envelope.Subscription is not null)
            {
                result.Add(envelope.Subscription);
            }
        }

        return result;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var envelope = new MaxioCreateSubscriptionEnvelope { Subscription = subscription };
        return (await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "/subscriptions.json", envelope, cancellationToken)).Subscription
            ?? throw new MaxioApiException("Maxio returned an empty subscription response.", HttpStatusCode.BadGateway);
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        return (await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"/subscriptions/{subscriptionId}.json",
            null,
            cancellationToken)).Subscription
            ?? throw new MaxioApiException("Maxio returned an empty subscription response.", HttpStatusCode.BadGateway);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Do not include Maxio's response body in the exception. It can contain customer or payment data.
            throw new MaxioApiException("Maxio rejected the billing request.", response.StatusCode);
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException("Maxio returned an empty response.", HttpStatusCode.BadGateway);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        ValidateConfiguration();
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com"
            : _options.BaseUrl!.TrimEnd('/');

        var request = new HttpRequestMessage(method, new Uri(baseUrl + path, UriKind.Absolute));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio billing is not configured. Set the Maxio configuration secrets.");
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl) &&
            (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string message, HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; init; } = string.Empty;
    [JsonPropertyName("customer_reference")] public string CustomerReference { get; init; } = string.Empty;
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; init; } = "remittance";
    [JsonPropertyName("reference")] public string Reference { get; init; } = string.Empty;
    [JsonPropertyName("uniqueness_token")] public string UniquenessToken { get; init; } = string.Empty;
}

public sealed class MaxioProduct
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; init; } = string.Empty;
    [JsonPropertyName("trial_interval")] public int? TrialInterval { get; init; }
    [JsonPropertyName("trial_interval_unit")] public string? TrialIntervalUnit { get; init; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; init; }
    public bool Taxable { get; init; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; init; }
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
    [JsonPropertyName("product_price_in_cents")] public long? ProductPriceInCents { get; init; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string? Reference { get; init; }
    [JsonPropertyName("customer_id")] public long? CustomerId { get; init; }
    public MaxioProduct? Product { get; init; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; init; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; init; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; init; }
}

internal sealed class MaxioCreateCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCreateCustomer Customer { get; init; } = new();
}

internal sealed class MaxioCreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscription Subscription { get; init; } = new();
}
