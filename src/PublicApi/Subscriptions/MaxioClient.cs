using System;
using System.Collections.Generic;
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

/// <summary>
/// A small, contract-focused client for the Maxio OpenAPI operations used by
/// subscriptions. Request paths, envelopes, auth, and response fields map to
/// maxio-spec/openapi.yaml.
/// </summary>
internal sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly AuthenticationHeaderValue _authorization;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.ApiKey}:x")));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        // The spec permits product_family_id to be a handle when prefixed by "handle:".
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        return await GetArrayAsync<MaxioProductEnvelope, MaxioProduct>(path, item => item.Product, "listProductsForProductFamily", cancellationToken);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "readCustomerByReference");
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(response.StatusCode, "readCustomerByReference returned an invalid body");
    }

    public Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken) =>
        SendEnvelopeAsync<MaxioCustomerEnvelope, MaxioCustomer>(
            HttpMethod.Post,
            "customers.json",
            new { customer = new { first_name = firstName, last_name = lastName, email, reference } },
            item => item.Customer,
            "createCustomer",
            cancellationToken);

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
        GetArrayAsync<MaxioSubscriptionEnvelope, MaxioSubscription>($"customers/{customerId}/subscriptions.json", item => item.Subscription, "listCustomerSubscriptions", cancellationToken);

    public Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken) =>
        SendEnvelopeAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            HttpMethod.Post,
            "subscriptions.json",
            // The configured demo products do not require a payment profile. The
            // contract's remittance collection method records the receivable
            // without attempting automatic card capture at signup.
            new { subscription = new { product_handle = productHandle, customer_id = customerId, reference, payment_collection_method = "remittance" } },
            item => item.Subscription,
            "createSubscription",
            cancellationToken);

    private async Task<IReadOnlyList<TValue>> GetArrayAsync<TEnvelope, TValue>(string path, Func<TEnvelope, TValue?> select, string operation, CancellationToken cancellationToken)
        where TValue : class
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, operation);
        var items = await response.Content.ReadFromJsonAsync<List<TEnvelope>>(JsonOptions, cancellationToken)
            ?? throw new MaxioApiException(response.StatusCode, $"{operation} returned an invalid body");

        var results = new List<TValue>(items.Count);
        foreach (var item in items)
        {
            var value = select(item);
            if (value is not null) results.Add(value);
        }
        return results;
    }

    private async Task<TValue> SendEnvelopeAsync<TEnvelope, TValue>(HttpMethod method, string path, object body, Func<TEnvelope, TValue?> select, string operation, CancellationToken cancellationToken)
        where TValue : class
    {
        using var response = await SendAsync(method, path, JsonContent.Create(body), cancellationToken);
        await EnsureSuccessAsync(response, operation);
        var envelope = await response.Content.ReadFromJsonAsync<TEnvelope>(JsonOptions, cancellationToken);
        return envelope is not null && select(envelope) is TValue value
            ? value
            : throw new MaxioApiException(response.StatusCode, $"{operation} returned an invalid body");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = _authorization;
        // Ownership transfers to HttpRequestMessage; do not dispose content separately.
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, operation);
        }
        return Task.CompletedTask;
    }
}
