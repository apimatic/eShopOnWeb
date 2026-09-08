using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. Every endpoint, request/response
/// shape and error model used here is defined by the Maxio OpenAPI specification that is
/// the authoritative contract for Maxio interactions.
/// </summary>
public sealed class MaxioApiClient
{
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient http, MaxioOptions options, ILogger<MaxioApiClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        _options.EnsureValid();
        _http.BaseAddress = new Uri(_options.GetBaseUrl());
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("product_families.json", cancellationToken);
        await EnsureSuccessAsync(response, "product_families.json", cancellationToken);

        var envelopes = await ReadJsonAsync<List<ProductFamilyEnvelope>>(response, cancellationToken);
        return SelectNonNull(envelopes, e => e.ProductFamily);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var url = $"product_families/{productFamilyId}/products.json?per_page={MaxPageSize}&page={page}";
            using var response = await _http.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, url, cancellationToken);

            var envelopes = await ReadJsonAsync<List<ProductEnvelope>>(response, cancellationToken);
            var items = SelectNonNull(envelopes, e => e.Product);
            products.AddRange(items);

            if (items.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioProduct?> ReadProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var url = $"products/handle/{Uri.EscapeDataString(handle)}.json";
        using var response = await _http.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, url, cancellationToken);
        var envelope = await ReadJsonAsync<ProductEnvelope>(response, cancellationToken);
        return envelope?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _http.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, url, cancellationToken);
        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerRequest
        {
            Customer = new CreateCustomerData
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _http.PostAsync("customers.json", JsonContent(payload), cancellationToken);
        await EnsureSuccessAsync(response, "customers.json", cancellationToken);

        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException("Maxio returned an empty customer payload.", (int)response.StatusCode, null, "customers.json");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionData
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                // The demo catalog requires no payment method on file; use the invoice-based
                // "remittance" collection method so signup succeeds without card capture / 3-DS.
                PaymentCollectionMethod = "remittance"
            }
        };

        using var response = await _http.PostAsync("subscriptions.json", JsonContent(payload), cancellationToken);
        await EnsureSuccessAsync(response, "subscriptions.json", cancellationToken);

        var envelope = await ReadJsonAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException("Maxio returned an empty subscription payload.", (int)response.StatusCode, null, "subscriptions.json");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        using var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, url, cancellationToken);

        var envelopes = await ReadJsonAsync<List<SubscriptionEnvelope>>(response, cancellationToken);
        return SelectNonNull(envelopes, e => e.Subscription);
    }

    private static StringContent JsonContent<T>(T payload)
    {
        return new StringContent(JsonSerializer.Serialize(payload, s_jsonOptions), Encoding.UTF8, "application/json");
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(body, s_jsonOptions);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string requestUri, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Maxio API request to {RequestUri} failed with status {StatusCode}: {Body}",
            requestUri, (int)response.StatusCode, body);

        throw new MaxioApiException(
            $"Maxio API request to '{requestUri}' failed with status {(int)response.StatusCode}.", 
            (int)response.StatusCode, body, requestUri);
    }

    private static IReadOnlyList<TItem> SelectNonNull<TEnvelope, TItem>(List<TEnvelope>? envelopes, Func<TEnvelope, TItem?> selector)
        where TItem : class
    {
        var items = new List<TItem>();
        if (envelopes is null)
        {
            return items;
        }

        foreach (var envelope in envelopes)
        {
            var item = selector(envelope);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }
}
