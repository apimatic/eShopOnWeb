using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> implementation of <see cref="IMaxioApiClient"/>. Authentication,
/// base address and default headers are configured on the injected <see cref="HttpClient"/> by the
/// DI registration; this class is responsible for path construction, (de)serialization and mapping
/// non-success responses to <see cref="MaxioApiException"/>.
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private const int PageSize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            // The product_family_id path segment accepts a handle when prefixed with "handle:".
            var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json" +
                       $"?page={page}&per_page={PageSize}";

            using var response = await _httpClient.GetAsync(path, cancellationToken);
            await EnsureSuccessAsync(response, "list products for product family", cancellationToken);

            var batch = await DeserializeAsync<List<ProductEnvelope>>(response, cancellationToken);
            if (batch is null || batch.Count == 0)
            {
                break;
            }

            foreach (var envelope in batch)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await _httpClient.GetAsync(path, cancellationToken);

        // A missing reference yields 404; that is an expected "not found", not an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer by reference", cancellationToken);

        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest { Customer = customer };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, "create customer", new[] { "Response did not contain a customer." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";

        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var batch = await DeserializeAsync<List<SubscriptionEnvelope>>(response, cancellationToken);
        if (batch is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(batch.Count);
        foreach (var envelope in batch)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest { Subscription = subscription };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var envelope = await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, "create subscription", new[] { "Response did not contain a subscription." });
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        _logger.LogWarning($"Maxio {operation} returned {(int)response.StatusCode}: {body}");
        throw new MaxioApiException(response.StatusCode, operation, errors);
    }

    /// <summary>
    /// Extracts human-readable error messages from a Maxio error body. Handles the documented
    /// <c>{"errors":["msg", ...]}</c> shape as well as the object form <c>{"errors":{"field":["msg"]}}</c>
    /// and a plain <c>{"error":"msg"}</c> fallback.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errors.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                messages.Add(item.GetString()!);
                            }
                        }
                        break;
                    case JsonValueKind.Object:
                        foreach (var field in errors.EnumerateObject())
                        {
                            if (field.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in field.Value.EnumerateArray())
                                {
                                    messages.Add($"{field.Name}: {item.GetString()}");
                                }
                            }
                            else
                            {
                                messages.Add($"{field.Name}: {field.Value}");
                            }
                        }
                        break;
                }

                if (messages.Count > 0)
                {
                    return messages;
                }
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var single) &&
                single.ValueKind == JsonValueKind.String)
            {
                return new[] { single.GetString()! };
            }
        }
        catch (JsonException)
        {
            // Body was not JSON (e.g. a plain-text 404). Fall through to returning the raw body.
        }

        return new[] { body.Trim() };
    }
}
