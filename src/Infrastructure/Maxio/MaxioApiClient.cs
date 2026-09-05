using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. Every request/response shape here is
/// taken from maxio-spec/openapi.yaml; nothing is invented beyond what that contract documents.
/// </summary>
public class MaxioApiClient
{
    private const int ProductsPageSize = 200;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain)))
        {
            throw new BillingProviderException(
                "Maxio is not configured. Set Maxio:ApiKey and either Maxio:Subdomain or Maxio:BaseUrl " +
                "(via user-secrets/environment) before using subscription billing.");
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfUnsuccessfulAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerAttributes attributes, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json", new CreateCustomerEnvelope { Customer = attributes }, cancellationToken);

        await ThrowIfUnsuccessfulAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer ?? throw new BillingProviderException("Maxio returned an empty customer payload.");
    }

    public async Task<MaxioProductFamily> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"product_families/handle:{Uri.EscapeDataString(handle)}.json", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingProviderException(
                $"Maxio product family '{handle}' was not found on this site. Check Maxio:ProductFamilyHandle.",
                response.StatusCode);
        }

        await ThrowIfUnsuccessfulAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ProductFamilyEnvelope>(cancellationToken: cancellationToken);
        return envelope?.ProductFamily ?? throw new BillingProviderException("Maxio returned an empty product family payload.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            using var response = await _httpClient.GetAsync(
                $"products.json?page={page}&per_page={ProductsPageSize}", cancellationToken);
            await ThrowIfUnsuccessfulAsync(response, cancellationToken);

            var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(cancellationToken: cancellationToken)
                ?? new List<ProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < ProductsPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfUnsuccessfulAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionAttributes attributes, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "subscriptions.json", new CreateSubscriptionEnvelope { Subscription = attributes }, cancellationToken);

        await ThrowIfUnsuccessfulAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Subscription ?? throw new BillingProviderException("Maxio returned an empty subscription payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await ThrowIfUnsuccessfulAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(cancellationToken: cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private static async Task ThrowIfUnsuccessfulAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new BillingProviderException(
            $"Maxio API call to {response.RequestMessage?.RequestUri} failed with {(int)response.StatusCode} {response.StatusCode}: {ExtractErrorSummary(body)}",
            response.StatusCode,
            new[] { body });
    }

    private static string ExtractErrorSummary(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(no response body)";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through and return the raw body below.
        }

        return body;
    }
}
