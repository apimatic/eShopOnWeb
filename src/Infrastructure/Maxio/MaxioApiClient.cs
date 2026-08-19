using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP access to the Maxio Advanced Billing REST API (Basic auth: API key as username, "x" as password).
/// </summary>
internal sealed class MaxioApiClient
{
    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static void Configure(HttpClient httpClient, MaxioSettings settings, string? environment)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio:ApiKey is not configured. Set MAXIO_API_KEY or the Maxio:ApiKey user-secret.");
        }

        var baseUrl = MaxioBaseUrlResolver.Resolve(settings, environment);
        httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return GetOrDefaultAsync<MaxioCustomerEnvelope, MaxioCustomer>(path, envelope => envelope.Customer, cancellationToken);
    }

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        return PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope, MaxioCustomer>(
            "customers.json",
            request,
            envelope => envelope.Customer,
            cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var familyId = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var path = $"product_families/{familyId}/products.json?per_page=200&page=1";
        var envelopes = await GetRequiredAsync<List<MaxioProductEnvelope>>(path, cancellationToken);
        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return GetOrDefaultAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(path, envelope => envelope.Subscription, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await GetRequiredAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken);
        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        return PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope, MaxioSubscription>(
            "subscriptions.json",
            request,
            envelope => envelope.Subscription,
            cancellationToken);
    }

    private async Task<TResult?> GetOrDefaultAsync<TEnvelope, TResult>(
        string path,
        Func<TEnvelope, TResult?> selector,
        CancellationToken cancellationToken)
        where TResult : class
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<TEnvelope>(response, cancellationToken);
        return envelope is null ? null : selector(envelope);
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await DeserializeAsync<T>(response, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response body.");
    }

    private async Task<TResult> PostAsync<TRequest, TEnvelope, TResult>(
        string path,
        TRequest body,
        Func<TEnvelope, TResult?> selector,
        CancellationToken cancellationToken)
        where TResult : class
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<TEnvelope>(response, cancellationToken)
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response body.");
        return selector(envelope)
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned a response without the expected resource.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryFormatMaxioError(body) ?? $"Maxio API request failed with {(int)response.StatusCode} {response.ReasonPhrase}.";
        throw new MaxioApiException(response.StatusCode, message);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, MaxioJson.SerializerOptions, cancellationToken);
    }

    internal static string? TryFormatMaxioError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<MaxioErrorPayload>(body, MaxioJson.SerializerOptions);
            if (payload?.Errors is { Count: > 0 })
            {
                return string.Join(" ", payload.Errors);
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body.
        }

        return body.Length > 500 ? body[..500] : body;
    }
}
