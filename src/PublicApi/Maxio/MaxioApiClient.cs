using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/>. The injected <see cref="HttpClient"/>
/// is expected to already be configured (via DI, see Program.cs) with the Maxio base address
/// and Basic Auth credentials, per maxio-spec/openapi.yaml's server/security definitions.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new CreateMaxioCustomerEnvelope { Customer = request };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, SerializerOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference is unique per site; a 422 here most likely means a concurrent
            // request already created this customer. Treat that as success (idempotent).
            var existing = await LookupCustomerByReferenceAsync(request.Reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Customer;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(SerializerOptions, cancellationToken);
        return envelopes?.Select(e => e.Product).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(SerializerOptions, cancellationToken);
        return envelopes?.Select(e => e.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new CreateMaxioSubscriptionEnvelope { Subscription = request };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, SerializerOptions, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Subscription;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Maxio API call {Method} {Uri} failed with status {StatusCode}: {Body}",
            response.RequestMessage?.Method,
            response.RequestMessage?.RequestUri,
            (int)response.StatusCode,
            body);

        throw new MaxioApiException((int)response.StatusCode, body);
    }
}
