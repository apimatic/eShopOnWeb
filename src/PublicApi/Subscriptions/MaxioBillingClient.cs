using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var products = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?page={page}&per_page=200";
            var pageProducts = await GetAsync<List<MaxioProductResponse>>(path, cancellationToken);
            if (pageProducts is null || pageProducts.Count == 0)
                break;

            foreach (var product in pageProducts)
            {
                if (product.Product is not null && product.Product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Product.Handle))
                    products.Add(product.Product);
            }

            if (pageProducts.Count < 200)
                break;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return await GetOptionalAsync<MaxioCustomerResponse>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken)
            is { Customer: not null } response ? response.Customer : null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await PostAsync<MaxioCustomerResponse>("customers.json", request, cancellationToken);
        return response.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned no customer.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await GetAsync<List<MaxioSubscriptionResponse>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
        if (response is null)
            return subscriptions;

        foreach (var item in response)
        {
            if (item.Subscription is not null)
                subscriptions.Add(item.Subscription);
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return await GetOptionalAsync<MaxioSubscriptionResponse>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken)
            is { Subscription: not null } response ? response.Subscription : null;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await PostAsync<MaxioSubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return response.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned no subscription.");
    }

    private void EnsureConfigured()
    {
        _options.Validate();
        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = _options.GetBaseUri();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw MaxioApiException.FromResponse(response.StatusCode, content);

        var result = JsonSerializer.Deserialize<T>(content, JsonOptions);
        return result ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }
}

public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static MaxioApiException FromResponse(HttpStatusCode statusCode, string content)
    {
        try
        {
            var error = JsonSerializer.Deserialize<MaxioErrorResponse>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (error?.Errors is { Count: > 0 })
                return new MaxioApiException(statusCode, string.Join("; ", error.Errors));
        }
        catch (JsonException)
        {
            // Preserve the upstream status even if an upstream error is not valid JSON.
        }

        return new MaxioApiException(statusCode, $"Maxio returned HTTP {(int)statusCode}.");
    }
}

public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("trial_interval")] public int? TrialInterval { get; set; }
    [JsonPropertyName("trial_interval_unit")] public string? TrialIntervalUnit { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
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

public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("customer_id")] public int CustomerId { get; set; }
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = string.Empty;
}

public sealed class MaxioErrorResponse
{
    [JsonPropertyName("errors")] public List<string>? Errors { get; set; }
}
