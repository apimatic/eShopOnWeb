using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// HttpClient-backed implementation of <see cref="IMaxioApiClient"/>. Base address and Basic auth are
/// configured on the injected <see cref="HttpClient"/> at registration time. Non-success responses are
/// translated into <see cref="SubscriptionBillingException"/> carrying the provider's error messages.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        // The path segment accepts either a numeric id or a handle prefixed with "handle:".
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "list plans", cancellationToken);

        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        var products = new List<MaxioProduct>();
        foreach (var envelope in envelopes ?? new List<MaxioProductEnvelope>())
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        // A missing reference is a normal outcome, not an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer", cancellationToken);

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        using var content = JsonContent.Create(request, options: MaxioJson.Options);
        using var response = await _httpClient.PostAsync("customers.json", content, cancellationToken);
        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new SubscriptionBillingException("Maxio returned an empty customer response when creating a customer.");
        }

        return envelope.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        using var content = JsonContent.Create(request, options: MaxioJson.Options);
        using var response = await _httpClient.PostAsync("subscriptions.json", content, cancellationToken);
        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new SubscriptionBillingException("Maxio returned an empty subscription response when creating a subscription.");
        }

        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes ?? new List<MaxioSubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        => await response.Content.ReadFromJsonAsync<T>(MaxioJson.Options, cancellationToken);

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadBodyAsync(response, cancellationToken);
        var errors = MaxioErrorParser.Parse(body);

        _logger.LogWarning(
            "Maxio request to {Operation} failed with status {StatusCode}. Errors: {Errors}",
            operation, (int)response.StatusCode, errors.Count > 0 ? string.Join("; ", errors) : "(none)");

        throw new SubscriptionBillingException(
            $"Failed to {operation} (Maxio returned HTTP {(int)response.StatusCode}).",
            errors);
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
