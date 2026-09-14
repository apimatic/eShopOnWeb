using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioClient"/> implemented over a typed <see cref="HttpClient"/> whose
/// base address and HTTP Basic authorization are configured during registration
/// (see <c>MaxioServiceCollectionExtensions</c>).
/// </summary>
public class MaxioClient : IMaxioClient
{
    /// <summary>Shared serializer settings: Maxio uses snake_case JSON on the wire.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "listProductsForProductFamily", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken)
                        ?? new List<ProductEnvelope>();

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

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "readCustomerByReference", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerEnvelope { Customer = customer };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "createCustomer", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(response.StatusCode, "createCustomer",
                new[] { "Customer creation returned an empty body." }, null);
        }

        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "listCustomerSubscriptions", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken)
                        ?? new List<SubscriptionEnvelope>();

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

    public async Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "findSubscription", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionEnvelope { Subscription = subscription };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "createSubscription", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(response.StatusCode, "createSubscription",
                new[] { "Subscription creation returned an empty body." }, null);
        }

        return envelope.Subscription;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        throw new MaxioApiException(response.StatusCode, operation, errors, body);
    }

    /// <summary>
    /// Extracts messages from the spec's error models: Error-List-Response
    /// (<c>{ "errors": [ ... ] }</c>) and Customer-Error-Response
    /// (<c>{ "errors": { "customer": "..." } }</c>). Falls back to the raw body.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return ReadErrorElement(errorsElement, body);
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to raw body.
        }

        return new[] { body.Trim() };
    }

    private static IReadOnlyList<string> ReadErrorElement(JsonElement errorsElement, string body)
    {
        var messages = new List<string>();
        switch (errorsElement.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in errorsElement.EnumerateArray())
                {
                    var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        messages.Add(value!);
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in errorsElement.EnumerateObject())
                {
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                    messages.Add($"{property.Name}: {value}");
                }
                break;
            case JsonValueKind.String:
                var single = errorsElement.GetString();
                if (!string.IsNullOrWhiteSpace(single))
                {
                    messages.Add(single!);
                }
                break;
        }

        return messages.Count > 0 ? messages : new[] { body.Trim() };
    }
}
