using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing REST API.
/// </summary>
/// <remarks>
/// <para>
/// Base address, HTTP Basic credentials and retry policy are configured on the injected
/// <see cref="HttpClient"/> by <c>AddMaxioBilling</c>; this class only knows paths and payloads.
/// </para>
/// <para>
/// Endpoints used, all verified against Maxio's generated .NET SDK
/// (https://github.com/maxio-com/ab-dotnet-sdk) and against a live Advanced Billing sandbox:
/// <c>GET /site.json</c>,
/// <c>GET /product_families/handle:{handle}/products.json</c>,
/// <c>GET /customers/lookup.json</c>,
/// <c>POST /customers.json</c>,
/// <c>GET /customers/{id}/subscriptions.json</c>,
/// <c>POST /subscriptions.json</c>,
/// <c>GET /subscriptions/lookup.json</c>.
/// </para>
/// </remarks>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio echoes a request id on every response; it is the handle their support asks for.</summary>
    private const string RequestIdHeader = "X-Request-Id";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Maxio occasionally renders numeric fields as strings; accept both rather than fail a read.
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(
            HttpMethod.Get, "site.json", content: null, treatNotFoundAsNull: false, cancellationToken).ConfigureAwait(false);

        return envelope?.Site ?? throw new MaxioApiException(
            HttpMethod.Get, "site.json", HttpStatusCode.OK, new[] { "Response did not contain a site." });
    }

    public async Task<IReadOnlyList<MaxioProduct>?> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // Maxio accepts either a numeric id or the "handle:my-family" form in the id position.
        // Handles are stable across catalogue re-seeds; ids are not, so always address by handle.
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";

        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get, path, content: null, treatNotFoundAsNull: true, cancellationToken).ConfigureAwait(false);

        return envelopes?
            .Select(e => e.Product)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get, path, content: null, treatNotFoundAsNull: true, cancellationToken).ConfigureAwait(false);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            treatNotFoundAsNull: false,
            cancellationToken).ConfigureAwait(false);

        return envelope?.Customer ?? throw new MaxioApiException(
            HttpMethod.Post, "customers.json", HttpStatusCode.OK, new[] { "Response did not contain a customer." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";

        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get, path, content: null, treatNotFoundAsNull: true, cancellationToken).ConfigureAwait(false);

        return envelopes?
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList()
            ?? (IReadOnlyList<MaxioSubscription>)Array.Empty<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            treatNotFoundAsNull: false,
            cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription ?? throw new MaxioApiException(
            HttpMethod.Post, "subscriptions.json", HttpStatusCode.OK, new[] { "Response did not contain a subscription." });
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get, path, content: null, treatNotFoundAsNull: true, cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription;
    }

    /// <summary>
    /// Issues one API call and deserializes the response.
    /// </summary>
    /// <param name="treatNotFoundAsNull">
    /// When true, HTTP 404 yields <c>null</c> rather than an exception. Maxio uses 404 to mean
    /// "no such record" on its lookup endpoints, which is an expected answer, not a failure.
    /// </param>
    /// <exception cref="MaxioApiException">Maxio returned a non-success status.</exception>
    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? content,
        bool treatNotFoundAsNull,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, path);

        if (content is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(content, content.GetType(), SerializerOptions),
                Encoding.UTF8,
                "application/json");
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var requestId = GetRequestId(response);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Maxio {Method} {Path} -> {StatusCode} in {ElapsedMs}ms (request id {RequestId}).",
            method,
            path,
            (int)response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            requestId ?? "n/a");

        if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errors = MaxioErrorParser.Parse(body);

            _logger.LogWarning(
                "Maxio {Method} {Path} failed with {StatusCode} (request id {RequestId}): {Errors}",
                method,
                path,
                (int)response.StatusCode,
                requestId ?? "n/a",
                errors.Count > 0 ? string.Join(" ", errors) : "no error detail");

            throw new MaxioApiException(method, path, response.StatusCode, errors, requestId);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                method,
                path,
                response.StatusCode,
                new[] { $"Response body could not be read as {typeof(TResponse).Name}: {ex.Message}" },
                requestId);
        }
    }

    private static string? GetRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues(RequestIdHeader, out var values) ? values.FirstOrDefault() : null;
}
