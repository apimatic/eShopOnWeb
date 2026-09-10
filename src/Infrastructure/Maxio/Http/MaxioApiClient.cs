using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Typed <see cref="HttpClient"/> implementation of <see cref="IMaxioApiClient"/> targeting the
/// Maxio Advanced Billing REST API. Authentication is HTTP Basic (API key as username, "x" as password).
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var maxio = settings.Value;
        if (!maxio.IsConfigured)
        {
            throw new SubscriptionConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:ProductFamilyHandle and Maxio:Subdomain (or Maxio:BaseUrl).");
        }

        // Ensure a trailing slash so relative endpoint paths combine correctly with any base (incl. a verbatim BaseUrl override).
        var baseUri = maxio.ResolveBaseUri();
        _httpClient.BaseAddress = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxio.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
        _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<ProductWire>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        // The product_family path segment accepts the family handle prefixed with "handle:".
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "list products for family", cancellationToken).ConfigureAwait(false);

        var envelopes = await DeserializeAsync<List<ProductEnvelope>>(response, cancellationToken).ConfigureAwait(false);
        var products = new List<ProductWire>();
        foreach (var envelope in envelopes ?? new List<ProductEnvelope>())
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<CustomerWire?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer by reference", cancellationToken).ConfigureAwait(false);
        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope?.Customer;
    }

    public async Task<CustomerWire> CreateCustomerAsync(CreateCustomerWire customer, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerEnvelope { Customer = customer };
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.PostAsync("customers.json", content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "create customer", cancellationToken).ConfigureAwait(false);

        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope?.Customer
            ?? throw new SubscriptionBillingException("Maxio returned an empty customer payload when creating a customer.");
    }

    public async Task<SubscriptionWire> CreateSubscriptionAsync(CreateSubscriptionWire subscription, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionEnvelope { Subscription = subscription };
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.PostAsync("subscriptions.json", content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "create subscription", cancellationToken).ConfigureAwait(false);

        var envelope = await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope?.Subscription
            ?? throw new SubscriptionBillingException("Maxio returned an empty subscription payload when creating a subscription.");
    }

    public async Task<IReadOnlyList<SubscriptionWire>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken).ConfigureAwait(false);

        var envelopes = await DeserializeAsync<List<SubscriptionEnvelope>>(response, cancellationToken).ConfigureAwait(false);
        var subscriptions = new List<SubscriptionWire>();
        foreach (var envelope in envelopes ?? new List<SubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Throws a <see cref="SubscriptionBillingException"/> carrying the provider's error messages when the
    /// response is not successful. The API key is never included in the exception.
    /// </summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var error = JsonSerializer.Deserialize<MaxioErrorEnvelope>(raw, JsonOptions);
                    if (error?.Errors is { Count: > 0 })
                    {
                        detail = string.Join("; ", error.Errors);
                    }
                }
                catch (JsonException)
                {
                    // Non-standard error body; fall back to the raw text (truncated).
                }

                detail ??= raw.Length > 500 ? raw[..500] : raw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Maxio error response body for operation {Operation}.", operation);
        }

        _logger.LogError("Maxio request to {Operation} failed with status {StatusCode}. {Detail}",
            operation, (int)response.StatusCode, detail);

        var message = $"Maxio failed to {operation} (HTTP {(int)response.StatusCode})."
            + (detail is null ? string.Empty : $" {detail}");
        throw new SubscriptionBillingException(message);
    }
}
