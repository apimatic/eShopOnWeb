using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the operations of maxio-spec/openapi.yaml that this integration uses.
/// Authentication, base address, timeouts and retries are configured on the injected
/// <see cref="HttpClient"/>; see <see cref="MaxioServiceCollectionExtensions"/>.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Guards against an unbounded page walk if the service keeps returning full pages.</summary>
    private const int MaxProductPages = 25;

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(
        HttpClient httpClient,
        IOptionsMonitor<MaxioSettings> settings,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productFamilyHandle);

        var pageSize = _settings.CurrentValue.EffectivePageSize;
        var products = new List<MaxioProduct>();

        // "Either the product family's id or its handle prefixed with `handle:`"
        // (maxio-spec/components/parameters/product-family-id-path.yaml).
        var familySegment = Uri.EscapeDataString("handle:" + productFamilyHandle.Trim());

        for (var page = 1; page <= MaxProductPages; page++)
        {
            var url = $"product_families/{familySegment}/products.json" +
                      $"?page={page}&per_page={pageSize}&include_archived=false";

            var pageItems = await SendAsync<List<MaxioProductResponse>>(
                HttpMethod.Get,
                url,
                content: null,
                operationId: "listProductsForProductFamily",
                treatNotFoundAsNull: false,
                cancellationToken).ConfigureAwait(false);

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

            if (pageItems.Count < pageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            content: null,
            operationId: "readCustomerByReference",
            treatNotFoundAsNull: true,
            cancellationToken).ConfigureAwait(false);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            operationId: "createCustomer",
            treatNotFoundAsNull: false,
            cancellationToken).ConfigureAwait(false);

        return response?.Customer
            ?? throw new MaxioApiException(
                "createCustomer",
                HttpStatusCode.OK,
                new[] { "The response did not contain a customer." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            content: null,
            operationId: "listCustomerSubscriptions",
            treatNotFoundAsNull: true,
            cancellationToken).ConfigureAwait(false);

        if (responses is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(responses.Count);
        foreach (var response in responses)
        {
            if (response.Subscription is not null)
            {
                subscriptions.Add(response.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            content: null,
            operationId: "findSubscription",
            treatNotFoundAsNull: true,
            cancellationToken).ConfigureAwait(false);

        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            operationId: "createSubscription",
            treatNotFoundAsNull: false,
            cancellationToken).ConfigureAwait(false);

        return response?.Subscription
            ?? throw new MaxioApiException(
                "createSubscription",
                HttpStatusCode.Created,
                new[] { "The response did not contain a subscription." });
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string relativeUrl,
        object? content,
        string operationId,
        bool treatNotFoundAsNull,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, content.GetType(), options: MaxioJson.Options);
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            _logger.LogError(
                ex,
                "Maxio {OperationId} ({Method} {Path}) failed in transport after {ElapsedMs} ms.",
                operationId,
                method,
                PathOnly(relativeUrl),
                stopwatch.ElapsedMilliseconds);

            throw new MaxioTransportException(operationId, ex.Message, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            _logger.LogInformation(
                "Maxio {OperationId} ({Method} {Path}) responded {StatusCode} in {ElapsedMs} ms.",
                operationId,
                method,
                PathOnly(relativeUrl),
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MaxioApiException(
                    operationId,
                    response.StatusCode,
                    MaxioErrorParser.Parse(body),
                    Truncate(body));
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, MaxioJson.Options);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(
                    operationId,
                    response.StatusCode,
                    new[] { "The response body could not be read: " + ex.Message },
                    Truncate(body),
                    ex);
            }
        }
    }

    /// <summary>Strips the query string so references and handles never reach the logs.</summary>
    private static string PathOnly(string relativeUrl)
    {
        var queryStart = relativeUrl.IndexOf('?');
        return queryStart < 0 ? relativeUrl : relativeUrl[..queryStart];
    }

    private static string Truncate(string? body) =>
        body is { Length: > 2000 } ? body[..2000] + "..." : body ?? string.Empty;
}
