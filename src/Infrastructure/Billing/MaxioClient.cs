using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioClient
{
    private static readonly SemaphoreSlim MaxioConcurrencyGate = new(4, 4);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200&include_archived=false";
        return (await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken))
            .Select(item => item.Product)
            .ToList();
    }

    public async Task<MaxioProduct?> FindProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var response = await SendAllowNotFoundAsync<MaxioProductEnvelope>(HttpMethod.Get, path, null, cancellationToken);
        return response?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAllowNotFoundAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
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
        return (await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken)).Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAllowNotFoundAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken);
        return response?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        return (await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken))
            .Select(item => item.Subscription)
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                payment_collection_method = "remittance",
                reference = subscriptionReference
            }
        };
        return (await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken)).Subscription;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var result = await SendAllowNotFoundAsync<T>(method, path, body, cancellationToken);
        return result ?? throw new MaxioApiException(HttpStatusCode.NotFound, new[] { "The requested Maxio resource was not found." });
    }

    private async Task<T?> SendAllowNotFoundAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        await MaxioConcurrencyGate.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(response, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
            return value ?? throw new MaxioApiException(
                response.StatusCode,
                new[] { "Maxio returned an empty or invalid response." });
        }
        finally
        {
            MaxioConcurrencyGate.Release();
        }
    }

    private static async Task<MaxioApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<MaxioErrorResponse>(stream, JsonOptions, cancellationToken);
            var errors = error?.Errors?.Where(message => !string.IsNullOrWhiteSpace(message)).ToList();
            return new MaxioApiException(
                response.StatusCode,
                errors is { Count: > 0 } ? errors : new[] { "Maxio rejected the request." });
        }
        catch (JsonException)
        {
            return new MaxioApiException(response.StatusCode, new[] { "Maxio rejected the request." });
        }
    }
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("product_family")]
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public sealed class MaxioProductFamily
{
    public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public MaxioCustomer Customer { get; set; } = new();
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioErrorResponse
{
    public List<string>? Errors { get; set; }
}
