using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Typed <see cref="HttpClient"/> for the Maxio Advanced Billing API, built directly against the OpenAPI
/// specification in <c>maxio-spec/</c>.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Page size used when walking paginated collections. The specification caps <c>per_page</c> at 200.</summary>
    private const int PageSize = 200;

    /// <summary>Guard against an unbounded pagination loop if the API ever stops shrinking the page.</summary>
    private const int MaxPages = 50;

    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<SiteResponse>("site.json", cancellationToken).ConfigureAwait(false);
        return response?.Site ?? throw MalformedBody(HttpMethod.Get, "site.json", "site");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productFamilyId);

        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{Uri.EscapeDataString(productFamilyId)}/products.json" +
                       $"?page={page.ToString(CultureInfo.InvariantCulture)}&per_page={PageSize.ToString(CultureInfo.InvariantCulture)}";

            var pageItems = await GetAsync<List<ProductResponse>>(path, cancellationToken).ConfigureAwait(false);
            if (pageItems is null || pageItems.Count == 0)
            {
                break;
            }

            foreach (var item in pageItems)
            {
                if (item.Product is not null)
                {
                    products.Add(item.Product);
                }
            }

            if (pageItems.Count < PageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        // The specification documents a single exact match; the API answers 404 when the reference is unknown.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadAsync<CustomerResponse>(response, HttpMethod.Get.Method, path, cancellationToken).ConfigureAwait(false);
        return payload?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await PostAsync<CreateCustomerRequest, CustomerResponse>("customers.json", request, cancellationToken).ConfigureAwait(false);
        return response?.Customer ?? throw MalformedBody(HttpMethod.Post, "customers.json", "customer");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

        var items = await GetAsync<List<SubscriptionResponse>>(path, cancellationToken).ConfigureAwait(false);
        if (items is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(items.Count);
        foreach (var item in items)
        {
            if (item.Subscription is not null)
            {
                subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await PostAsync<CreateSubscriptionRequest, SubscriptionResponse>("subscriptions.json", request, cancellationToken).ConfigureAwait(false);
        return response?.Subscription ?? throw MalformedBody(HttpMethod.Post, "subscriptions.json", "subscription");
    }

    private async Task<TResponse?> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync<TResponse>(response, HttpMethod.Get.Method, path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return await ReadAsync<TResponse>(response, HttpMethod.Post.Method, path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs}ms.",
                request.Method,
                DescribePath(request.RequestUri),
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Maxio {Method} {Path} failed after {ElapsedMs}ms.", request.Method, DescribePath(request.RequestUri), stopwatch.ElapsedMilliseconds);

            throw new MaxioApiException(
                HttpStatusCode.ServiceUnavailable,
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                new[] { "The Maxio API could not be reached." },
                ex);
        }
    }

    private static async Task<TResponse?> ReadAsync<TResponse>(HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new MaxioApiException(response.StatusCode, method, path, MaxioErrorReader.Read(body));
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        try
        {
            return await response.Content
                .ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                response.StatusCode,
                method,
                path,
                new[] { "The Maxio API returned a body that does not match the documented schema." },
                ex);
        }
    }

    /// <summary>
    /// Renders a request URI for logging without its query string, which can carry a shopper's e-mail
    /// address in the customer-lookup reference.
    /// </summary>
    private static string DescribePath(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return string.Empty;
        }

        return requestUri.IsAbsoluteUri
            ? requestUri.AbsolutePath
            : requestUri.OriginalString.Split('?')[0];
    }

    private static MaxioApiException MalformedBody(HttpMethod method, string path, string expectedProperty) =>
        new(
            HttpStatusCode.OK,
            method.Method,
            path,
            new[] { $"The Maxio API response did not contain the expected '{expectedProperty}' object." });
}
