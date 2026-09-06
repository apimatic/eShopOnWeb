using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <inheritdoc cref="IMaxioApiClient" />
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maximum page size accepted by the specification's <c>per_page</c> parameter.</summary>
    private const int MaxPerPage = 200;

    /// <summary>Guard against an unbounded paging loop if the server ever stops honouring <c>page</c>.</summary>
    private const int MaxPages = 50;

    /// <summary>How much of a failing response body is retained for logging.</summary>
    private const int MaxLoggedBodyLength = 2000;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default) =>
        SendAsync<MaxioSiteResponse, MaxioSite>(
            HttpMethod.Get,
            "site.json",
            content: null,
            operation: "readSite",
            select: response => response.Site,
            cancellationToken);

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json" +
                       $"?page={page.ToString(CultureInfo.InvariantCulture)}" +
                       $"&per_page={MaxPerPage.ToString(CultureInfo.InvariantCulture)}" +
                       $"&include_archived={(includeArchived ? "true" : "false")}";

            var pageResults = await SendAsync<List<MaxioProductResponse>, List<MaxioProductResponse>>(
                HttpMethod.Get,
                path,
                content: null,
                operation: "listProductsForProductFamily",
                select: response => response,
                cancellationToken).ConfigureAwait(false);

            foreach (var wrapper in pageResults)
            {
                if (wrapper.Product is not null)
                {
                    products.Add(wrapper.Product);
                }
            }

            if (pageResults.Count < MaxPerPage)
            {
                return products;
            }
        }

        _logger.LogWarning(
            "Stopped paging Maxio products for family {ProductFamily} after {MaxPages} pages.",
            productFamilyIdOrHandle,
            MaxPages);

        return products;
    }

    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        SendOrNullAsync<MaxioCustomerResponse, MaxioCustomer>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            content: null,
            operation: "readCustomerByReference",
            select: response => response.Customer,
            cancellationToken);

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MaxioCustomerResponse, MaxioCustomer>(
            HttpMethod.Post,
            "customers.json",
            JsonContent.Create(request, options: MaxioJson.Options),
            operation: "createCustomer",
            select: response => response.Customer,
            cancellationToken);

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var wrappers = await SendAsync<List<MaxioSubscriptionResponse>, List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json",
            content: null,
            operation: "listCustomerSubscriptions",
            select: response => response,
            cancellationToken).ConfigureAwait(false);

        var subscriptions = new List<MaxioSubscription>(wrappers.Count);
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Subscription is not null)
            {
                subscriptions.Add(wrapper.Subscription);
            }
        }

        return subscriptions;
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        SendOrNullAsync<MaxioSubscriptionResponse, MaxioSubscription>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            content: null,
            operation: "findSubscription",
            select: response => response.Subscription,
            cancellationToken);

    public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MaxioSubscriptionResponse, MaxioSubscription>(
            HttpMethod.Post,
            "subscriptions.json",
            JsonContent.Create(request, options: MaxioJson.Options),
            operation: "createSubscription",
            select: response => response.Subscription,
            cancellationToken);

    private async Task<TResult> SendAsync<TResponse, TResult>(
        HttpMethod method,
        string path,
        HttpContent? content,
        string operation,
        Func<TResponse, TResult?> select,
        CancellationToken cancellationToken)
        where TResult : class
    {
        var result = await SendCoreAsync(method, path, content, operation, select, treatNotFoundAsNull: false, cancellationToken)
            .ConfigureAwait(false);

        return result ?? throw new MaxioApiException(
            operation,
            HttpStatusCode.OK,
            new[] { "The billing provider returned a success status with an empty payload." });
    }

    private Task<TResult?> SendOrNullAsync<TResponse, TResult>(
        HttpMethod method,
        string path,
        HttpContent? content,
        string operation,
        Func<TResponse, TResult?> select,
        CancellationToken cancellationToken)
        where TResult : class =>
        SendCoreAsync(method, path, content, operation, select, treatNotFoundAsNull: true, cancellationToken);

    private async Task<TResult?> SendCoreAsync<TResponse, TResult>(
        HttpMethod method,
        string path,
        HttpContent? content,
        string operation,
        Func<TResponse, TResult?> select,
        bool treatNotFoundAsNull,
        CancellationToken cancellationToken)
        where TResult : class
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // A per-request timeout surfaces as OperationCanceledException with the caller's token
            // still unset, so both transport failures are reported the same way.
            throw new MaxioApiException(
                operation,
                HttpStatusCode.ServiceUnavailable,
                new[] { "The billing provider could not be reached." },
                rawBody: null,
                innerException: ex);
        }

        using (response)
        {
            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = Truncate(await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false));
                var errors = MaxioErrorParser.Parse(body);

                _logger.LogWarning(
                    "Maxio {Operation} ({Method} {Path}) returned HTTP {StatusCode}: {Body}",
                    operation,
                    method.Method,
                    path,
                    (int)response.StatusCode,
                    body);

                throw new MaxioApiException(operation, response.StatusCode, errors, body);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            try
            {
                var payload = await response.Content
                    .ReadFromJsonAsync<TResponse>(MaxioJson.Options, cancellationToken)
                    .ConfigureAwait(false);

                return payload is null ? null : select(payload);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(
                    operation,
                    response.StatusCode,
                    new[] { "The billing provider returned a response that could not be parsed." },
                    rawBody: null,
                    innerException: ex);
            }
        }
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return null;
        }
    }

    private static string? Truncate(string? body) =>
        body is { Length: > MaxLoggedBodyLength } ? body[..MaxLoggedBodyLength] + "..." : body;
}
