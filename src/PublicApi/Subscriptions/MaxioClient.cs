using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _baseUrl = options.Value.ResolveBaseUrl();

        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(BuildUri("site.json"), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSiteEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Site ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty site response.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        using var response = await _httpClient.GetAsync(
            BuildUri($"product_families/{family}/products.json?per_page=200&include_archived=false"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(JsonOptions, cancellationToken);
        return items?.Select(x => x.Product).ToList() ?? [];
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            BuildUri($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerInput customer,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            },
            uniqueness_token = uniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync(BuildUri("customers.json"), payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            BuildUri($"customers/{customerId}/subscriptions.json"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken);
        return items?.Select(x => x.Subscription).ToList() ?? [];
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            BuildUri($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string subscriptionReference,
        string paymentCollectionMethod,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                payment_collection_method = paymentCollectionMethod
            },
            uniqueness_token = uniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync(BuildUri("subscriptions.json"), payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription response.");
    }

    private Uri BuildUri(string relativePath)
    {
        var separator = _baseUrl.EndsWith("/", StringComparison.Ordinal) ? string.Empty : "/";
        return new Uri($"{_baseUrl}{separator}{relativePath}", UriKind.Absolute);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ReadErrorAsync(response, cancellationToken);
        throw new MaxioApiException(response.StatusCode, message);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var values = errors.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString())
                        .Where(x => !string.IsNullOrWhiteSpace(x));
                    var joined = string.Join("; ", values!);
                    if (!string.IsNullOrWhiteSpace(joined))
                    {
                        return joined;
                    }
                }

                if (errors.ValueKind == JsonValueKind.String)
                {
                    return errors.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // Upstream proxies may return a non-JSON error page; do not reflect it to callers.
        }

        return $"Maxio request failed with HTTP {(int)response.StatusCode}.";
    }
}
