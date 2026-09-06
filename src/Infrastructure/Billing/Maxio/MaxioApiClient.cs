using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <inheritdoc cref="IMaxioApiClient"/>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Largest page size the Advanced Billing list endpoints accept.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Stop paging after this many pages so a provider bug cannot spin a request forever.</summary>
    private const int MaxPages = 50;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly string _baseAddress;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Paths are appended to the configured base address rather than resolved against it, so a
        // base URL that carries its own path prefix (a gateway, a proxy) keeps that prefix.
        _baseAddress = options.Value.ResolveBaseAddress().AbsoluteUri.TrimEnd('/');
    }

    public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default) =>
        GetRequiredAsync<MaxioSiteEnvelope, MaxioSite>("/site.json", e => e.Site, cancellationToken);

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        // A product family can be addressed by numeric id or by "handle:<handle>"; handles are the stable key.
        var path = $"/product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        return GetAllPagesAsync<MaxioProductEnvelope, MaxioProduct>(path, e => e.Product, cancellationToken);
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return GetOrNullAsync<MaxioCustomerEnvelope, MaxioCustomer>(path, e => e.Customer, cancellationToken);
    }

    public Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default) =>
        PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope, MaxioCustomer>(
            "/customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            e => e.Customer,
            cancellationToken);

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return GetOrNullAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            path,
            e => e.Subscription,
            cancellationToken);
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default) =>
        PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope, MaxioSubscription>(
            "/subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            e => e.Subscription,
            cancellationToken);

    public Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/subscriptions.json?customer_id={customerId.ToString(CultureInfo.InvariantCulture)}";
        return GetAllPagesAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            path,
            e => e.Subscription,
            cancellationToken);
    }

    private async Task<TResult> GetRequiredAsync<TEnvelope, TResult>(
        string path,
        Func<TEnvelope, TResult> unwrap,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);
        return unwrap(await ReadAsync<TEnvelope>(response, HttpMethod.Get, path, cancellationToken));
    }

    private async Task<TResult?> GetOrNullAsync<TEnvelope, TResult>(
        string path,
        Func<TEnvelope, TResult> unwrap,
        CancellationToken cancellationToken)
        where TResult : class
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);
        return unwrap(await ReadAsync<TEnvelope>(response, HttpMethod.Get, path, cancellationToken));
    }

    private async Task<IReadOnlyList<TResult>> GetAllPagesAsync<TEnvelope, TResult>(
        string path,
        Func<TEnvelope, TResult> unwrap,
        CancellationToken cancellationToken)
    {
        var separator = path.Contains('?') ? '&' : '?';
        var results = new List<TResult>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var pagedPath = $"{path}{separator}page={page}&per_page={MaxPageSize}";

            using var response = await SendAsync(HttpMethod.Get, pagedPath, content: null, cancellationToken);
            await EnsureSuccessAsync(response, HttpMethod.Get, pagedPath, cancellationToken);

            var envelopes = await ReadAsync<List<TEnvelope>>(response, HttpMethod.Get, pagedPath, cancellationToken);
            results.AddRange(envelopes.Select(unwrap));

            if (envelopes.Count < MaxPageSize)
            {
                return results;
            }
        }

        _logger.LogWarning(
            "Maxio returned more than {MaxItems} items for {Path}; the list was truncated.",
            MaxPages * MaxPageSize,
            path);

        return results;
    }

    private async Task<TResult> PostAsync<TRequest, TEnvelope, TResult>(
        string path,
        TRequest body,
        Func<TEnvelope, TResult> unwrap,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, options: MaxioJson.Options);
        using var response = await SendAsync(HttpMethod.Post, path, content, cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Post, path, cancellationToken);
        return unwrap(await ReadAsync<TEnvelope>(response, HttpMethod.Post, path, cancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, _baseAddress + path)
        {
            Content = content
        };

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // An HttpClient timeout surfaces as OperationCanceledException without the caller's token
            // being cancelled, so it is reported as an availability problem rather than a cancellation.
            throw new SubscriptionBillingUnavailableException(
                $"The Maxio API could not be reached ({method} {path}).", ex);
        }
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(MaxioJson.Options, cancellationToken);
            if (value is null)
            {
                throw new SubscriptionBillingUnavailableException(
                    $"The Maxio API returned an empty body for {method} {path}.");
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingUnavailableException(
                $"The Maxio API returned a response that could not be parsed for {method} {path}.", ex);
        }
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var requestId = response.Headers.TryGetValues("X-Request-Id", out var values)
            ? values.FirstOrDefault()
            : null;

        var body = await ReadBodySafelyAsync(response, cancellationToken);
        var errors = ParseErrors(body);

        _logger.LogWarning(
            "Maxio API {Method} {Path} returned {StatusCode}. Request id: {RequestId}. Errors: {Errors}",
            method.Method,
            path,
            (int)response.StatusCode,
            requestId ?? "(none)",
            errors.Count > 0 ? string.Join("; ", errors) : "(none)");

        throw new MaxioApiException(response.StatusCode, method.Method, path, errors, requestId, body);
    }

    private static async Task<string?> ReadBodySafelyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Flattens both error shapes Advanced Billing uses: a list of messages
    /// (<c>{"errors":["..."]}</c>) and messages keyed by field (<c>{"errors":{"field":["..."]}}</c>).
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<MaxioErrorListResponse>(body, MaxioJson.Options);
            if (list?.Errors is { Count: > 0 })
            {
                return list.Errors;
            }
        }
        catch (JsonException)
        {
            // Not the list shape; try the keyed shape below.
        }

        try
        {
            var map = JsonSerializer.Deserialize<MaxioErrorMapResponse>(body, MaxioJson.Options);
            if (map?.Errors is { Count: > 0 })
            {
                return map.Errors
                    .SelectMany(pair => pair.Value.Select(message => $"{pair.Key}: {message}"))
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Not JSON, or not a shape we know: fall back to the raw body below.
        }

        return new[] { body.Length > 500 ? body[..500] : body };
    }
}
