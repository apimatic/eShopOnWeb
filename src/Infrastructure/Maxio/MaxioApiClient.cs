using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed HTTP client for the Maxio Advanced Billing REST API.
/// </summary>
public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide Maxio:ApiKey (and Maxio:Subdomain) via user-secrets or environment configuration.");
        }

        _httpClient.BaseAddress = _settings.GetApiBaseUri();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(_settings.ApiKey + ":x")));
    }

    internal async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken).ConfigureAwait(false);
        return envelope.Site ?? throw new MaxioApiException(200, "Maxio site payload was empty.");
    }

    internal async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await GetAsync<List<MaxioProductFamilyEnvelope>>("product_families.json", cancellationToken).ConfigureAwait(false);
        return envelopes.Where(e => e.ProductFamily is not null).Select(e => e.ProductFamily!).ToList();
    }

    internal async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(int productFamilyId, CancellationToken cancellationToken = default)
    {
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/{productFamilyId}/products.json", cancellationToken).ConfigureAwait(false);
        return envelopes.Where(e => e.Product is not null).Select(e => e.Product!).ToList();
    }

    internal async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var envelope = Deserialize<MaxioCustomerEnvelope>(payload);
        return envelope.Customer;
    }

    internal async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerFields customer, CancellationToken cancellationToken = default)
    {
        var payload = SendAsync<MaxioCreateCustomerEnvelope, MaxioCustomerEnvelope>(
            HttpMethod.Post, "customers.json", new MaxioCreateCustomerEnvelope { Customer = customer }, cancellationToken);
        var envelope = await payload.ConfigureAwait(false);
        return envelope.Customer ?? throw new MaxioApiException(200, "Maxio customer payload was empty.");
    }

    internal async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken).ConfigureAwait(false);
        return envelopes.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    internal async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionFields subscription, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCreateSubscriptionEnvelope, MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", new MaxioCreateSubscriptionEnvelope { Subscription = subscription }, cancellationToken).ConfigureAwait(false);
        return envelope.Subscription ?? throw new MaxioApiException(200, "Maxio subscription payload was empty.");
    }

    private async Task<TResponse> GetAsync<TResponse>(string requestUri, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        var payload = await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return Deserialize<TResponse>(payload);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string requestUri, TRequest body, CancellationToken cancellationToken)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(body, MaxioJson.Options);
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return Deserialize<TResponse>(payload);
    }

    private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw MaxioApiException.FromPayload((int)response.StatusCode, payload);
        }

        return payload;
    }

    private static T Deserialize<T>(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(payload, MaxioJson.Options)!;
    }
}
