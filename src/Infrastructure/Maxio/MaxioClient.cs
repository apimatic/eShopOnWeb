using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioClient"/> over a configured <see cref="HttpClient"/> (base address and Basic
/// auth are set up by DI). Serialization uses snake_case to match the Maxio OpenAPI schemas.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    private const string HandlePrefix = "handle:";
    private const int ProductsPageSize = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle, CancellationToken cancellationToken = default)
    {
        var segment = BuildFamilySegment(productFamilyIdOrHandle);
        var products = new List<MaxioProduct>();

        for (var page = 1; ; page++)
        {
            var relative = $"product_families/{segment}/products.json?per_page={ProductsPageSize}&page={page}";
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(relative, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var env in envelopes)
            {
                if (env.Product is not null)
                {
                    products.Add(env.Product);
                }
            }

            if (envelopes.Count < ProductsPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var relative = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, relative);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerEnvelope { Customer = customer };
        var envelope = await PostAsync<MaxioCreateCustomerEnvelope, MaxioCustomerEnvelope>(
            "customers.json", body, cancellationToken);

        return envelope?.Customer
               ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Create customer returned an empty body." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        var relative = $"customers/{customerId}/subscriptions.json";
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(relative, cancellationToken)
                        ?? new List<MaxioSubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>();
        foreach (var env in envelopes)
        {
            if (env.Subscription is not null)
            {
                subscriptions.Add(env.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionEnvelope { Subscription = subscription };
        var envelope = await PostAsync<MaxioCreateSubscriptionEnvelope, MaxioSubscriptionEnvelope>(
            "subscriptions.json", body, cancellationToken);

        return envelope?.Subscription
               ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Create subscription returned an empty body." });
    }

    private static string BuildFamilySegment(string idOrHandle)
    {
        if (idOrHandle.StartsWith(HandlePrefix, StringComparison.Ordinal))
        {
            return HandlePrefix + Uri.EscapeDataString(idOrHandle.Substring(HandlePrefix.Length));
        }

        return Uri.EscapeDataString(idOrHandle);
    }

    private async Task<T?> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string relativeUri, TRequest body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, MaxioApiException.ParseErrors(body));
    }
}
