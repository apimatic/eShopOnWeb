using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin wrapper over the Maxio Advanced Billing REST API (customers, subscriptions,
/// product families). Authentication (HTTP Basic, API key as username) and the base
/// address are configured on the injected <see cref="HttpClient"/> by
/// <see cref="MaxioServiceCollectionExtensions"/>.
/// </summary>
internal class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ProductPayload>> ListProductFamilyProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<ProductListItem>>(JsonOptions, cancellationToken) ?? new();
        return items
            .Select(i => i.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<SitePayload> GetSiteAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("site.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SiteEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Site ?? throw new MaxioApiException("Maxio returned an empty site payload from site.json.");
    }

    public async Task<CustomerPayload?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers.json?q={Uri.EscapeDataString(reference)}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<CustomerListItem>>(JsonOptions, cancellationToken) ?? new();
        return items
            .Select(i => i.Customer)
            .FirstOrDefault(c => c is not null && string.Equals(c.Reference, reference, StringComparison.Ordinal));
    }

    public async Task<CustomerPayload> CreateCustomerAsync(CreateCustomerPayload customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequestEnvelope { Customer = customer, UniquenessToken = uniquenessToken };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new MaxioDuplicateRequestException();
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException("Maxio returned an empty customer payload from customers.json.");
    }

    public async Task<IReadOnlyList<SubscriptionPayload>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/{customerId}/subscriptions.json?per_page=200",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<SubscriptionListItem>>(JsonOptions, cancellationToken) ?? new();
        return items
            .Select(i => i.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<SubscriptionPayload> CreateSubscriptionAsync(CreateSubscriptionPayload subscription, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequestEnvelope { Subscription = subscription, UniquenessToken = uniquenessToken };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new MaxioDuplicateRequestException();
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException("Maxio returned an empty subscription payload from subscriptions.json.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(
            $"Maxio API call to {response.RequestMessage?.RequestUri} failed with status {(int)response.StatusCode} ({response.StatusCode}): {body}",
            (int)response.StatusCode);
    }
}
