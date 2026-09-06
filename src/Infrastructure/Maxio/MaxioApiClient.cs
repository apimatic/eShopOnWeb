using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <inheritdoc cref="IMaxioApiClient"/>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps <c>per_page</c> at 200; anything larger is silently clamped.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guards against an unbounded loop if a site ever stops honouring pagination.</summary>
    private const int MaxPages = 25;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", allowNotFound: true, cancellationToken);
        return envelope?.Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        // A family is addressable by id or by "handle:" prefixed handle. Handles are the stable choice.
        var familySegment = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{familySegment}/products.json?page={page}&per_page={MaxPageSize}";
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, allowNotFound: false, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            products.AddRange(envelopes.Select(envelope => envelope.Product).OfType<MaxioProduct>());

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetAsync<MaxioCustomerEnvelope>(path, allowNotFound: true, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateMaxioCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<MaxioCustomerEnvelope>("customers.json", request, cancellationToken);

        return envelope?.Customer
               ?? throw new MaxioApiException(
                   HttpStatusCode.OK,
                   HttpMethod.Post.Method,
                   "customers.json",
                   new[] { "Maxio accepted the customer but returned no customer in the response body." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(path, allowNotFound: true, cancellationToken);

        return envelopes?.Select(envelope => envelope.Subscription).OfType<MaxioSubscription>().ToArray()
               ?? Array.Empty<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", request, cancellationToken);

        return envelope?.Subscription
               ?? throw new MaxioApiException(
                   HttpStatusCode.OK,
                   HttpMethod.Post.Method,
                   "subscriptions.json",
                   new[] { "Maxio accepted the subscription but returned no subscription in the response body." });
    }

    private async Task<TResponse?> GetAsync<TResponse>(
        string path,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get.Method, path, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TResponse>(
        string path,
        object body,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            // Buffered content, so the retry handler can replay the request as-is.
            Content = new StringContent(
                JsonSerializer.Serialize(body, body.GetType(), SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, HttpMethod.Post.Method, path, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            // Buffer the whole response: bodies are small, and it keeps the per-attempt timeout in the
            // retry handler covering the body as well as the headers.
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            // Retries are already exhausted by the time this surfaces.
            throw new MaxioTransportException(
                $"Could not reach Maxio for {request.Method} {request.RequestUri?.PathAndQuery}.",
                exception);
        }
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string method,
        string path,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = MaxioApiException.ParseErrors(body);

        // The body can echo customer details, so log the classification rather than the payload.
        _logger.LogWarning(
            "Maxio {Method} {Path} returned {StatusCode} with {ErrorCount} error(s).",
            method,
            path,
            (int)response.StatusCode,
            errors.Count);

        throw new MaxioApiException(response.StatusCode, method, path, errors);
    }

    private static async Task<TResponse?> ReadAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, SerializerOptions, cancellationToken);
    }
}

/// <summary>
/// Maxio could not be reached at all: DNS, TLS, connection or client-side timeout failures that
/// survived the retry policy.
/// </summary>
public class MaxioTransportException : Exception
{
    public MaxioTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
