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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, CancellationToken cancellationToken);
}

public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var productFamily = await GetWrappedAsync<MaxioProductFamily>(
            $"product_families/{EscapePathSegment($"handle:{_options.ProductFamilyHandle}")}.json", "product_family", cancellationToken);

        using var response = await _httpClient.GetAsync($"product_families/{productFamily.Id}/products.json", cancellationToken);
        var products = await ReadWrappedListAsync<MaxioProduct>(response, "product", cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null)
            .Select(product => new MaxioPlan(product.Handle, product.Name, product.Description, product.PriceInCents, product.Interval, product.IntervalUnit))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadWrappedAsync<MaxioCustomer>(response, "customer", cancellationToken);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("customers.json", new { customer }, JsonOptions, cancellationToken);
        return await ReadWrappedAsync<MaxioCustomer>(response, "customer", cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        return await ReadWrappedListAsync<MaxioSubscription>(response, "subscription", cancellationToken);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, CancellationToken cancellationToken)
    {
        var request = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                // The seeded plans do not require a payment method. Remittance preserves that
                // catalog policy and lets Maxio create the recurring invoice without card capture.
                payment_collection_method = "remittance"
            }
        };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        return await ReadWrappedAsync<MaxioSubscription>(response, "subscription", cancellationToken);
    }

    private async Task<T> GetWrappedAsync<T>(string path, string propertyName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadWrappedAsync<T>(response, propertyName, cancellationToken);
    }

    private static async Task<T> ReadWrappedAsync<T>(HttpResponseMessage response, string propertyName, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await MaxioApiException.CreateAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty(propertyName, out var item))
        {
            throw new MaxioApiException(response.StatusCode, "Maxio returned an unexpected response.");
        }

        return item.Deserialize<T>(JsonOptions)
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }

    private static async Task<IReadOnlyList<T>> ReadWrappedListAsync<T>(HttpResponseMessage response, string propertyName, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await MaxioApiException.CreateAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new MaxioApiException(response.StatusCode, "Maxio returned an unexpected response.");
        }

        var results = new List<T>();
        foreach (var wrapper in document.RootElement.EnumerateArray())
        {
            if (!wrapper.TryGetProperty(propertyName, out var item))
            {
                throw new MaxioApiException(response.StatusCode, "Maxio returned an unexpected response.");
            }

            var result = item.Deserialize<T>(JsonOptions);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    private static string EscapePathSegment(string value) => Uri.EscapeDataString(value);
}

public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static async Task<MaxioApiException> CreateAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new MaxioApiException(response.StatusCode, string.IsNullOrWhiteSpace(body) ? "Maxio rejected the request." : body[..Math.Min(body.Length, 1024)]);
    }
}

public sealed record MaxioPlan(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record MaxioCustomer(long Id, [property: JsonPropertyName("first_name")] string FirstName, [property: JsonPropertyName("last_name")] string LastName, string Email, string? Reference);
public sealed record MaxioCustomerDraft([property: JsonPropertyName("first_name")] string FirstName, [property: JsonPropertyName("last_name")] string LastName, string Email, string Reference);
public sealed record MaxioSubscription(long Id, string State, [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents, [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt, [property: JsonPropertyName("current_period_ends_at")] DateTimeOffset? CurrentPeriodEndsAt, MaxioProduct? Product);
internal sealed record MaxioProductFamily(long Id, string Handle);
public sealed record MaxioProduct(string Handle, string Name, string? Description, [property: JsonPropertyName("price_in_cents")] long PriceInCents, int Interval, [property: JsonPropertyName("interval_unit")] string IntervalUnit, [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt);
