using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for Maxio Advanced Billing, written against <c>maxio-spec/openapi.yaml</c>:
/// paths, query parameters, request/response envelopes and error bodies all come from that
/// specification.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>
    /// Serializer settings shared by requests and responses. Maxio uses snake_case JSON, so
    /// property names are declared explicitly on the models; unset request properties are omitted
    /// rather than sent as nulls.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Cap on how much of an error body is carried into an exception message.</summary>
    private const int MaxErrorBodyLength = 2048;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        int page,
        int perPage,
        CancellationToken cancellationToken = default)
    {
        // The spec's path parameter accepts the family id, or its handle prefixed with "handle:".
        var path = $"product_families/{EscapePathSegment(productFamilyIdOrHandle)}/products.json" +
                   $"?page={page}&per_page={perPage}";

        var envelopes = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get, path, content: null, allowNotFound: false, cancellationToken);

        var products = new List<MaxioProduct>();
        foreach (var envelope in envelopes ?? new List<MaxioProductResponse>())
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var envelope = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get, path, content: null, allowNotFound: true, cancellationToken);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerRequest { Customer = customer };

        var envelope = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post, "customers.json", body, allowNotFound: false, cancellationToken);

        return envelope?.Customer
            ?? throw new MaxioApiException("Maxio returned an empty customer for createCustomer.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";

        var envelopes = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get, path, content: null, allowNotFound: true, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes ?? new List<MaxioSubscriptionResponse>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionRequest { Subscription = subscription };

        var envelope = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post, "subscriptions.json", body, allowNotFound: false, cancellationToken);

        return envelope?.Subscription
            ?? throw new MaxioApiException("Maxio returned an empty subscription for createSubscription.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var envelope = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get, path, content: null, allowNotFound: true, cancellationToken);

        return envelope?.Subscription;
    }

    /// <summary>
    /// Issues a Maxio call and maps the outcome onto <typeparamref name="TResponse"/> or a
    /// <see cref="MaxioApiException"/>.
    /// </summary>
    /// <param name="allowNotFound">
    /// When true, a <c>404</c> yields <see langword="null"/> instead of throwing. Maxio's lookup
    /// operations use 404 to mean "no such record", which is an expected outcome here.
    /// </param>
    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        object? content,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (content is not null)
        {
            var json = JsonSerializer.Serialize(content, content.GetType(), SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogError(ex, "Maxio {Method} {Path} failed after {ElapsedMs}ms.", method, PathOnly(relativePath), stopwatch.ElapsedMilliseconds);
            throw new MaxioApiException($"Could not reach Maxio for {method} {PathOnly(relativePath)}.", innerException: ex);
        }

        using (response)
        {
            _logger.LogInformation(
                "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs}ms.",
                method,
                PathOnly(relativePath),
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await BuildFailureAsync(method, relativePath, response, cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (stream.ConfigureAwait(false))
            {
                try
                {
                    return await JsonSerializer.DeserializeAsync<TResponse>(stream, SerializerOptions, cancellationToken);
                }
                catch (JsonException ex)
                {
                    throw new MaxioApiException(
                        $"Maxio returned a response for {method} {PathOnly(relativePath)} that does not match the expected schema.",
                        response.StatusCode,
                        innerException: ex);
                }
            }
        }
    }

    private static async Task<MaxioApiException> BuildFailureAsync(
        HttpMethod method,
        string relativePath,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodySafelyAsync(response, cancellationToken);
        var errors = MaxioErrorReader.ReadErrors(body);

        var detail = errors.Count > 0 ? string.Join(" ", errors) : Truncate(body);

        var message = $"Maxio {method} {PathOnly(relativePath)} failed with {(int)response.StatusCode} {response.ReasonPhrase}."
            + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}");

        return new MaxioApiException(message, response.StatusCode, errors);
    }

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) || value.Length <= MaxErrorBodyLength
            ? value
            : value.Substring(0, MaxErrorBodyLength);

    /// <summary>Strips the query string so lookup references never reach a log message.</summary>
    private static string PathOnly(string relativePath)
    {
        var queryStart = relativePath.IndexOf('?');
        return queryStart < 0 ? relativePath : relativePath.Substring(0, queryStart);
    }

    /// <summary>
    /// Escapes a path segment while preserving the <c>handle:</c> prefix the specification defines
    /// for its id-or-handle path parameters.
    /// </summary>
    private static string EscapePathSegment(string value)
    {
        const string handlePrefix = "handle:";

        return value.StartsWith(handlePrefix, StringComparison.Ordinal)
            ? handlePrefix + Uri.EscapeDataString(value.Substring(handlePrefix.Length))
            : Uri.EscapeDataString(value);
    }
}
