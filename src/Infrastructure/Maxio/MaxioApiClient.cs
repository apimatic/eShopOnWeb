using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <inheritdoc cref="IMaxioApiClient"/>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio's maximum page size for list endpoints.</summary>
    private const int PageSize = 200;

    /// <summary>
    /// Upper bound on pages walked by a single list call. A shopper cannot realistically own 20 000
    /// subscriptions; the cap is here so a pagination contract change can never spin forever.
    /// </summary>
    private const int MaxPages = 100;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken).ConfigureAwait(false);
        return envelope?.Site ?? throw new MaxioApiException("Maxio returned an empty site payload.", null);
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        return ListPagedAsync<MaxioProductEnvelope, MaxioProduct>(path, e => e.Product, cancellationToken);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetOrNullAsync<MaxioCustomerEnvelope>(path, cancellationToken).ConfigureAwait(false);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerAttributes customer,
        CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json",
            new MaxioCreateCustomerRequest(customer),
            cancellationToken).ConfigureAwait(false);

        return envelope?.Customer ?? throw new MaxioApiException("Maxio returned an empty customer payload.", null);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        return ListPagedAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(path, e => e.Subscription, cancellationToken);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioSubscriptionAttributes subscription,
        CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest(subscription),
            cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription ?? throw new MaxioApiException("Maxio returned an empty subscription payload.", null);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetOrNullAsync<MaxioSubscriptionEnvelope>(path, cancellationToken).ConfigureAwait(false);
        return envelope?.Subscription;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GET that treats 404 as "absent" rather than an error. Maxio's lookup endpoints answer 404 when
    /// nothing matches the reference, which is a normal, expected outcome here.
    /// </summary>
    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: MaxioJson.Options)
        };

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Walks every page of a Maxio list endpoint. Maxio returns a bare JSON array of single-property
    /// envelopes and signals the end of the collection with a short page.
    /// </summary>
    private async Task<IReadOnlyList<TItem>> ListPagedAsync<TEnvelope, TItem>(
        string path,
        Func<TEnvelope, TItem?> unwrap,
        CancellationToken cancellationToken)
        where TItem : class
    {
        var separator = path.Contains('?') ? "&" : "?";
        var items = new List<TItem>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var pagedPath = $"{path}{separator}page={page}&per_page={PageSize}";
            var envelopes = await GetAsync<List<TEnvelope>>(pagedPath, cancellationToken).ConfigureAwait(false);

            if (envelopes is null || envelopes.Count == 0)
            {
                return items;
            }

            foreach (var envelope in envelopes)
            {
                var item = unwrap(envelope);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            if (envelopes.Count < PageSize)
            {
                return items;
            }
        }

        _logger.LogWarning(
            "Stopped paging {Path} after {MaxPages} pages; results may be truncated.", path, MaxPages);

        return items;
    }

    /// <summary>
    /// Sends the request, funnelling transport failures into <see cref="MaxioApiException"/> so that
    /// every way a Maxio call can fail — refused, timed out, or rejected — reaches callers as one type.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException("The Maxio request timed out.", null, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException("Could not reach Maxio.", null, innerException: ex);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        try
        {
            return await response.Content
                .ReadFromJsonAsync<T>(MaxioJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                "Maxio returned a response that could not be parsed.", response.StatusCode, innerException: ex);
        }
    }

    /// <summary>
    /// Translates a non-success response into <see cref="MaxioApiException"/>, preserving Maxio's own
    /// validation messages so the caller sees why the write was rejected.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await TryReadErrorsAsync(response, cancellationToken).ConfigureAwait(false);
        var detail = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase ?? "no detail";

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "Maxio rejected the API credentials. Check the Maxio:ApiKey and Maxio:Subdomain settings.",
            HttpStatusCode.TooManyRequests =>
                "Maxio rate limit exceeded; the request was not processed.",
            _ => $"Maxio request failed with {(int)response.StatusCode} {response.StatusCode}: {detail}"
        };

        throw new MaxioApiException(message, response.StatusCode, errors);
    }

    private static async Task<IReadOnlyList<string>> TryReadErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return Array.Empty<string>();
            }

            var trimmed = body.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                // Some failures (notably authentication) come back as plain text rather than JSON.
                return new[] { body.Trim() };
            }

            var envelope = JsonSerializer.Deserialize<MaxioErrorEnvelope>(body, MaxioJson.Options);
            if (envelope?.Errors is { Count: > 0 })
            {
                return envelope.Errors;
            }

            return string.IsNullOrWhiteSpace(envelope?.Error)
                ? Array.Empty<string>()
                : new[] { envelope!.Error! };
        }
        catch (JsonException)
        {
            // An unparseable error body must not mask the status code the caller needs.
            return Array.Empty<string>();
        }
    }
}
