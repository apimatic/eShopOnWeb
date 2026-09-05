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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Narrow client for the Maxio operations used by subscriptions. Request paths and
/// JSON envelopes mirror maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, CancellationToken cancellationToken);
}

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = settings.GetApiBaseUri();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        // The contract permits a product-family handle when it is prefixed by "handle:".
        var path = $"product_families/{Uri.EscapeDataString($"handle:{productFamilyHandle}")}/products.json?page=1&per_page=200";
        return await GetListAsync<MaxioProductResponse, MaxioProduct>(path, response => response.Product, cancellationToken);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadAsync<MaxioCustomerResponse>(response, cancellationToken)).Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken)).Subscription;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateCustomerRequest(customer);
        using var response = await _httpClient.PostAsync("customers.json", JsonContent.Create(body), cancellationToken);
        return (await ReadAsync<MaxioCustomerResponse>(response, cancellationToken)).Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        return await GetListAsync<MaxioSubscriptionResponse, MaxioSubscription>(
            $"customers/{customerId}/subscriptions.json", response => response.Subscription, cancellationToken);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, CancellationToken cancellationToken)
    {
        // The seeded catalog permits subscription without card capture. Invoice collection is an
        // OpenAPI-defined collection method that avoids attempting an immediate card charge.
        var body = new MaxioCreateSubscriptionRequest(new MaxioCreateSubscription(productHandle, customerId, "invoice", reference));
        using var response = await _httpClient.PostAsync("subscriptions.json", JsonContent.Create(body), cancellationToken);
        return (await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken)).Subscription;
    }

    private async Task<IReadOnlyList<TItem>> GetListAsync<TEnvelope, TItem>(string path, Func<TEnvelope, TItem> unwrap, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await JsonSerializer.DeserializeAsync<List<TEnvelope>>(await response.Content.ReadAsStreamAsync(cancellationToken), JsonOptions, cancellationToken);
        return items?.Select(unwrap).ToArray() ?? Array.Empty<TItem>();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(cancellationToken), JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        // Maxio error bodies can contain validation detail; retain it only for internal diagnostics.
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, body);
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody) : base($"Maxio returned {(int)statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}

public sealed record MaxioCreateCustomer(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string Reference);

public sealed record MaxioCreateCustomerRequest([property: JsonPropertyName("customer")] MaxioCreateCustomer Customer);
public sealed record MaxioCreateSubscription(
    [property: JsonPropertyName("product_handle")] string ProductHandle,
    [property: JsonPropertyName("customer_id")] long CustomerId,
    [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod,
    [property: JsonPropertyName("reference")] string Reference);
public sealed record MaxioCreateSubscriptionRequest([property: JsonPropertyName("subscription")] MaxioCreateSubscription Subscription);
public sealed record MaxioCustomerResponse([property: JsonPropertyName("customer")] MaxioCustomer Customer);
public sealed record MaxioProductResponse([property: JsonPropertyName("product")] MaxioProduct Product);
public sealed record MaxioSubscriptionResponse([property: JsonPropertyName("subscription")] MaxioSubscription Subscription);

public sealed record MaxioCustomer(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("reference")] string? Reference);

public sealed record MaxioProduct(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string? Handle,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_in_cents")] long PriceInCents,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("interval_unit")] string IntervalUnit,
    [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt);

public sealed record MaxioSubscription(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
    [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
    [property: JsonPropertyName("current_period_ends_at")] DateTimeOffset? CurrentPeriodEndsAt,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("product")] MaxioProduct Product);
