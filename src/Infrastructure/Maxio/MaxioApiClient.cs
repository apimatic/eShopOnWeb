using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioApiClient"/> over <see cref="HttpClient"/>. Authentication, base address, timeout
/// and retry policy are configured on the typed client (see <see cref="MaxioServiceCollectionExtensions"/>);
/// this class only knows about paths, payload shapes and status-code semantics, all taken from the
/// Maxio OpenAPI specification.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maximum records per page allowed by the specification's <c>per_page</c> parameter.</summary>
    private const int MaxPerPage = 200;

    /// <summary>Safety stop so a misbehaving catalog cannot spin the pagination loop forever.</summary>
    private const int MaxPages = 25;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioSiteResponse>(HttpMethod.Get, "site.json", null, cancellationToken);
        return response?.Site ?? throw new MaxioApiException(HttpMethod.Get, "site.json", HttpStatusCode.OK,
            new[] { "Maxio returned an empty site payload." });
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(productFamilyHandle));
        }

        // The specification documents the path segment as "either the product family's id or its
        // handle prefixed with `handle:`".
        var familySegment = $"handle:{Uri.EscapeDataString(productFamilyHandle.Trim())}";
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{familySegment}/products.json?page={page}&per_page={MaxPerPage}";
            var batch = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
            if (batch is null || batch.Count == 0)
            {
                break;
            }

            foreach (var envelope in batch)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (batch.Count < MaxPerPage)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, null, cancellationToken,
            treatNotFoundAsNull: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return response?.Customer ?? throw new MaxioApiException(HttpMethod.Post, "customers.json", HttpStatusCode.OK,
            new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var response = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in response ?? new List<MaxioSubscriptionResponse>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A subscription reference is required.", nameof(reference));
        }

        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken,
            treatNotFoundAsNull: true);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return response?.Subscription ?? throw new MaxioApiException(HttpMethod.Post, "subscriptions.json", HttpStatusCode.OK,
            new[] { "Maxio returned an empty subscription payload." });
    }

    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? payload,
        CancellationToken cancellationToken, bool treatNotFoundAsNull = false)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, path);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, payload.GetType(), options: MaxioSerialization.Options);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _logger.LogError(ex, "Maxio request {Method} {Path} could not be completed.", method.Method, PathOnly(path));
            throw new MaxioTransportException(method, PathOnly(path), ex);
        }

        using (response)
        {
            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodyAsync(response, cancellationToken);
                var errors = MaxioSerialization.ParseErrors(errorBody);
                _logger.LogWarning("Maxio request {Method} {Path} returned {StatusCode}: {Errors}",
                    method.Method, PathOnly(path), (int)response.StatusCode, string.Join("; ", errors));
                throw new MaxioApiException(method, PathOnly(path), response.StatusCode, errors);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(MaxioSerialization.Options, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Maxio request {Method} {Path} returned a payload that could not be read.",
                    method.Method, PathOnly(path));
                throw new MaxioApiException(method, PathOnly(path), response.StatusCode,
                    new[] { "The response could not be read as the shape declared by the Maxio specification." }, ex);
            }
        }
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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

    /// <summary>Strips the query string so references and other caller data never reach the logs.</summary>
    private static string PathOnly(string path)
    {
        var separator = path.IndexOf('?');
        return separator < 0 ? path : path.Substring(0, separator);
    }
}
