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
/// Thin, typed wrapper over the Maxio (Advanced Billing) REST API. Handles serialization and error
/// mapping; contains no business logic. The <see cref="HttpClient"/> is configured (base address and
/// HTTP Basic auth header) by DI — see <c>MaxioServiceExtensions</c>.
/// </summary>
internal class MaxioApiClient : IMaxioApiClient
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

    /// <summary>Lists the products (plans) belonging to a product family, by family handle.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        var envelopes = await ReadAsync<List<ProductEnvelope>>(response, "list products for family", cancellationToken).ConfigureAwait(false);

        var products = new List<MaxioProduct>();
        foreach (var envelope in envelopes ?? new List<ProductEnvelope>())
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    /// <summary>
    /// Looks up a customer by their unique reference. Returns <c>null</c> when no customer exists
    /// with that reference (Maxio responds 404).
    /// </summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<CustomerEnvelope>(response, "lookup customer by reference", cancellationToken).ConfigureAwait(false);
        return envelope?.Customer;
    }

    /// <summary>Creates a customer.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerInput input, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest { Customer = input };
        using var content = JsonContent(request);
        using var response = await _httpClient.PostAsync("customers.json", content, cancellationToken).ConfigureAwait(false);
        var envelope = await ReadAsync<CustomerEnvelope>(response, "create customer", cancellationToken).ConfigureAwait(false);

        if (envelope?.Customer is null)
        {
            throw MaxioApiException.FromResponse(response.StatusCode, null, "create customer");
        }

        return envelope.Customer;
    }

    /// <summary>
    /// Creates a subscription. An optional <paramref name="uniquenessToken"/> is sent as a top-level
    /// sibling of the subscription so Maxio rejects duplicate submissions within its 60-minute window.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionInput input, string? uniquenessToken = null, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest { Subscription = input, UniquenessToken = uniquenessToken };
        using var content = JsonContent(request);
        using var response = await _httpClient.PostAsync("subscriptions.json", content, cancellationToken).ConfigureAwait(false);
        var envelope = await ReadAsync<SubscriptionEnvelope>(response, "create subscription", cancellationToken).ConfigureAwait(false);

        if (envelope?.Subscription is null)
        {
            throw MaxioApiException.FromResponse(response.StatusCode, null, "create subscription");
        }

        return envelope.Subscription;
    }

    /// <summary>Lists all subscriptions belonging to a customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        var envelopes = await ReadAsync<List<SubscriptionEnvelope>>(response, "list customer subscriptions", cancellationToken).ConfigureAwait(false);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes ?? new List<SubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private static StringContent JsonContent<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw MaxioApiException.FromResponse(response.StatusCode, ParseErrors(body), operation);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, Array.Empty<string>(),
                $"Maxio API returned an unparseable response during {operation}: {ex.Message}");
        }
    }

    private static IReadOnlyList<string>? ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var error = JsonSerializer.Deserialize<MaxioErrorResponse>(body, JsonOptions);
            return error?.Errors;
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. an HTML error page); surface a trimmed snippet.
            var snippet = body.Length > 200 ? body[..200] : body;
            return new[] { snippet };
        }
    }
}
