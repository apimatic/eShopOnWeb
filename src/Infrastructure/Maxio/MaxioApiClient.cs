using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Hand-written client for the Maxio Advanced Billing operations used by the subscription capability.
/// Paths, parameters, payload shapes and error models are taken from the Maxio OpenAPI specification in
/// <c>maxio-spec/</c>; authentication is the specification's <c>BasicAuth</c> scheme (API key as username,
/// literal <c>x</c> as password) and is applied when the typed client is configured.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Spec maximum for the <c>per_page</c> parameter.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guards against an unbounded loop if the API ever stops honouring <c>page</c>.</summary>
    private const int MaxPages = 50;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Site> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<SiteResponse>("site.json", cancellationToken).ConfigureAwait(false);
        return response?.Site ?? throw UnexpectedBody("GET", "site.json");
    }

    public async Task<IReadOnlyList<ProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await GetAsync<List<ProductFamilyResponse>>("product_families.json", cancellationToken)
            .ConfigureAwait(false);

        var families = new List<ProductFamily>();
        foreach (var envelope in envelopes ?? new List<ProductFamilyResponse>())
        {
            if (envelope.ProductFamily is not null)
            {
                families.Add(envelope.ProductFamily);
            }
        }

        return families;
    }

    public async Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(
        int productFamilyId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var products = new List<Product>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = string.Format(
                CultureInfo.InvariantCulture,
                "product_families/{0}/products.json?page={1}&per_page={2}&include_archived={3}",
                productFamilyId,
                page,
                MaxPageSize,
                includeArchived ? "true" : "false");

            var envelopes = await GetAsync<List<ProductResponse>>(path, cancellationToken).ConfigureAwait(false);

            if (envelopes is null || envelopes.Count == 0)
            {
                break;
            }

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var path = "customers/lookup.json?reference=" + Uri.EscapeDataString(reference);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        // The lookup is the "does this customer exist yet" probe; a miss is an expected outcome, not a fault.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get.Method, path, cancellationToken).ConfigureAwait(false);

        var payload = await ReadAsync<CustomerResponse>(response, HttpMethod.Get.Method, path, cancellationToken).ConfigureAwait(false);
        return payload?.Customer;
    }

    public async Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = await PostAsync<CreateCustomerRequest, CustomerResponse>(
            "customers.json",
            new CreateCustomerRequest(customer),
            cancellationToken).ConfigureAwait(false);

        return payload?.Customer ?? throw UnexpectedBody("POST", "customers.json");
    }

    public async Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var payload = await PostAsync<CreateSubscriptionRequest, SubscriptionResponse>(
            "subscriptions.json",
            new CreateSubscriptionRequest(subscription),
            cancellationToken).ConfigureAwait(false);

        return payload?.Subscription ?? throw UnexpectedBody("POST", "subscriptions.json");
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = string.Format(CultureInfo.InvariantCulture, "customers/{0}/subscriptions.json", customerId);
        var envelopes = await GetAsync<List<SubscriptionResponse>>(path, cancellationToken).ConfigureAwait(false);

        var subscriptions = new List<Subscription>();
        foreach (var envelope in envelopes ?? new List<SubscriptionResponse>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private async Task<TResponse?> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, HttpMethod.Get.Method, path, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, HttpMethod.Get.Method, path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, mediaType: null, MaxioJson.Options)
        };

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, HttpMethod.Post.Method, path, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, HttpMethod.Post.Method, path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_httpClient.BaseAddress is null)
        {
            throw new BillingNotConfiguredException(
                "The Maxio integration is not configured. Provide 'Maxio:ApiKey', 'Maxio:Subdomain' (or 'Maxio:BaseUrl') and 'Maxio:ProductFamilyHandle'.");
        }

        var started = DateTimeOffset.UtcNow;

        try
        {
            var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs} ms.",
                request.Method,
                request.RequestUri,
                (int)response.StatusCode,
                (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderUnavailableException("Could not reach the Maxio API.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderUnavailableException("The Maxio API request timed out.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The status code on its own is enough to fail the call.
        }

        throw new MaxioApiException(response.StatusCode, method, path, MaxioErrorParser.Parse(body), body);
    }

    private static async Task<TResponse?> ReadAsync<TResponse>(HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, MaxioJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderUnavailableException(
                "Maxio returned a response for " + method + " " + path + " that did not match the expected schema.", ex);
        }
    }

    private static BillingProviderUnavailableException UnexpectedBody(string method, string path) =>
        new BillingProviderUnavailableException("Maxio returned an empty payload for " + method + " " + path + ".");
}
