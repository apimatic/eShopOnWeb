using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string userId, string productHandle, CancellationToken cancellationToken);
}

/// <summary>
/// Small, explicit client for the Advanced Billing HTTP API. The API key never leaves this server.
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var products = await GetAsync<List<MaxioProductEnvelope>>($"product_families/{family}/products.json", cancellationToken);
        return products
            .Where(x => x.Product is not null && x.Product.ArchivedAt is null)
            .Select(x => x.Product!)
            .ToList();
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await PostAsync<MaxioCustomerEnvelope>(
                "customers.json",
                new { customer = new { reference, email, first_name = "eShopOnWeb", last_name = "Shopper" } },
                uniquenessToken: CreateToken($"customer:{reference}"),
                cancellationToken);
            return response.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an incomplete customer response.");
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity || ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Customer references are unique in Maxio. A concurrent request may have created it.
            return await FindCustomerAsync(reference, cancellationToken)
                ?? throw new MaxioApiException(ex.StatusCode, "Maxio did not return the customer created by a concurrent request.");
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return subscriptions.Where(x => x.Subscription is not null).Select(x => x.Subscription!).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string userId, string productHandle, CancellationToken cancellationToken)
    {
        var reference = CreateSubscriptionReference(userId, productHandle);
        try
        {
            var response = await PostAsync<MaxioSubscriptionEnvelope>(
                "subscriptions.json",
                new { subscription = new { customer_id = customerId, product_handle = productHandle, reference, payment_collection_method = "remittance" } },
                CreateToken($"subscription:{reference}"),
                cancellationToken);
            return response.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an incomplete subscription response.");
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Advanced Billing rejects the same uniqueness_token for 60 minutes. Resolve the original write instead of retrying it.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var original = await FindSubscriptionAsync(reference, cancellationToken);
                if (original is not null)
                {
                    return original;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
            }

            throw new SubscriptionProvisioningInProgressException();
        }
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await GetOrNotFoundAsync<MaxioCustomerEnvelope>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return result?.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await GetOrNotFoundAsync<MaxioSubscriptionEnvelope>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return result?.Subscription;
    }

    private async Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var result = await SendAsync<T>(HttpMethod.Get, relativePath, null, cancellationToken);
        return result ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }

    private Task<T?> GetOrNotFoundAsync<T>(string relativePath, CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Get, relativePath, null, cancellationToken, allowNotFound: true);

    private async Task<T> PostAsync<T>(string relativePath, object body, string uniquenessToken, CancellationToken cancellationToken)
    {
        var separator = relativePath.Contains('?') ? '&' : '?';
        var result = await SendAsync<T>(HttpMethod.Post, $"{relativePath}{separator}uniqueness_token={Uri.EscapeDataString(uniquenessToken)}", body, cancellationToken);
        return result ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        var baseAddress = _options.GetBaseAddress();
        using var request = new HttpRequestMessage(method, new Uri(baseAddress, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X")));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MaxioApiException(response.StatusCode, $"Maxio returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio could not be reached.", exception);
        }
        catch (JsonException exception)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an unreadable response.", exception);
        }
    }

    private static string CreateSubscriptionReference(string userId, string productHandle) =>
        $"eshop-sub-{Hash($"{userId}:{productHandle}")}";

    private static string CreateToken(string value) => Hash(value);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
    public MaxioApiException(HttpStatusCode statusCode, string message, Exception innerException) : base(message, innerException) => StatusCode = statusCode;
    public HttpStatusCode StatusCode { get; }
}

public sealed class SubscriptionProvisioningInProgressException : Exception
{
    public SubscriptionProvisioningInProgressException() : base("The original subscription request is still being finalized. Retry this request shortly.") { }
}

public sealed class MaxioCustomerEnvelope { [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; init; } }
public sealed class MaxioProductEnvelope { [JsonPropertyName("product")] public MaxioProduct? Product { get; init; } }
public sealed class MaxioSubscriptionEnvelope { [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; init; } }

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public long Id { get; init; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("handle")] public string? Handle { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
    [JsonPropertyName("interval")] public int Interval { get; init; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; init; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("product_price_in_cents")] public long? ProductPriceInCents { get; init; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("product")] public MaxioSubscriptionProduct? Product { get; init; }
}

public sealed class MaxioSubscriptionProduct
{
    [JsonPropertyName("handle")] public string? Handle { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
}
