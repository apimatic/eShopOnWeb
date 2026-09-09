using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> implementation of <see cref="IMaxioApiClient"/>. The base address and
/// Basic authentication header are configured on the injected client (see the DI registration).
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // Maxio returns 404 when there is no customer with that reference.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, $"GET {path}", cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken)
    {
        const string path = "customers.json";
        var body = new MaxioCreateCustomerRequest { Customer = attributes };
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);

        await EnsureSuccessAsync(response, $"POST {path}", cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Customer creation returned an empty body." }, $"POST {path}");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, $"GET {path}", cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioProductEnvelope>>(response, cancellationToken) ?? new List<MaxioProductEnvelope>();

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

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, $"GET {path}", cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();

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

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, CancellationToken cancellationToken)
    {
        const string path = "subscriptions.json";
        var body = new MaxioCreateSubscriptionRequest { Subscription = attributes };
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);

        await EnsureSuccessAsync(response, $"POST {path}", cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Subscription creation returned an empty body." }, $"POST {path}");
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Throws a <see cref="MaxioApiException"/> carrying the status code and any error messages Maxio
    /// returned, when the response is not a success status.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string requestDescription, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, ExtractErrors(payload), requestDescription);
    }

    /// <summary>
    /// Maxio error bodies come in a few shapes: {"errors":["msg", ...]}, {"errors":{"field":"msg"}},
    /// or {"errors":"msg"}. Extract human-readable messages from any of them, tolerating non-JSON.
    /// </summary>
    private static IReadOnlyList<string> ExtractErrors(string payload)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                CollectMessages(errors, messages);
            }
        }
        catch (JsonException)
        {
            // Non-JSON body (e.g. an HTML error page); fall back to the raw text.
            messages.Add(payload.Length > 500 ? payload[..500] : payload);
        }

        return messages;
    }

    private static void CollectMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMessages(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectMessages(property.Value, messages);
                }
                break;
        }
    }
}
