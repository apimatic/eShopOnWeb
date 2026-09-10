using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> implementation of <see cref="IMaxioApiClient"/>.
/// The client's <see cref="HttpClient.BaseAddress"/> and Basic-auth header are configured at
/// registration time (see <see cref="MaxioServiceCollectionExtensions"/>).
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> GetProductFamiliesAsync(CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioProductFamilyEnvelope>>(
            "product_families.json", cancellationToken).ConfigureAwait(false);

        return envelopes is null
            ? Array.Empty<MaxioProductFamily>()
            : envelopes.Where(e => e.ProductFamily is not null).Select(e => e.ProductFamily!).ToList();
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsByFamilyAsync(int productFamilyId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/{productFamilyId}/products.json", cancellationToken).ConfigureAwait(false);

        return envelopes is null
            ? Array.Empty<MaxioProduct>()
            : envelopes.Where(e => e.Product is not null).Select(e => e.Product!).ToList();
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, $"GET {path}", cancellationToken).ConfigureAwait(false);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CustomerInput input, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequest { Customer = input };
        var envelope = await PostAsync<CreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json", body, cancellationToken).ConfigureAwait(false);

        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Customer creation returned an empty body." }, "POST customers.json");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(SubscriptionInput input, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest { Subscription = input };
        var envelope = await PostAsync<CreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json", body, cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Subscription creation returned an empty body." }, "POST subscriptions.json");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken).ConfigureAwait(false);

        return envelopes is null
            ? Array.Empty<MaxioSubscription>()
            : envelopes.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"GET {path}", cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"POST {path}", cancellationToken).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string requestSummary, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ParseErrorsAsync(response, cancellationToken).ConfigureAwait(false);
        throw new MaxioApiException(response.StatusCode, errors, requestSummary);
    }

    private static async Task<IReadOnlyList<string>> ParseErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<string>();
            }

            // Maxio returns errors either as { "errors": [...] } or { "errors": { "field": [...] } }.
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return FlattenErrors(errorsElement);
            }

            return new[] { raw };
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> FlattenErrors(JsonElement errorsElement)
    {
        var messages = new List<string>();

        if (errorsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in errorsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    messages.Add(item.GetString()!);
                }
            }
        }
        else if (errorsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in errorsElement.EnumerateObject())
            {
                if (field.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in field.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            messages.Add($"{field.Name}: {item.GetString()}");
                        }
                    }
                }
            }
        }

        return messages;
    }
}
