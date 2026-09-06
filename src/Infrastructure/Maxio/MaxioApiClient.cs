using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/>, written against maxio-spec/openapi.yaml.
/// Paths, parameters, payload shapes and the Basic auth scheme all come from that specification.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>
    /// Maxio's JSON uses snake_case, so every contract property carries an explicit
    /// <see cref="JsonPropertyNameAttribute"/> rather than relying on a naming policy. Members the
    /// site returns but the spec does not model are ignored, so an unmodelled field cannot break a read.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptionsMonitor<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default) =>
        GetOrNullAsync<MaxioSiteEnvelope, MaxioSite>("site.json", e => e.Site, cancellationToken);

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var pageSize = _options.CurrentValue.PageSize;
        var products = new List<MaxioProduct>();

        // The spec exposes page/per_page on this operation but returns no total count, so pages are
        // walked until one comes back short.
        for (var page = 1; ; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json" +
                       $"?page={page.ToString(CultureInfo.InvariantCulture)}" +
                       $"&per_page={pageSize.ToString(CultureInfo.InvariantCulture)}";

            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < pageSize)
            {
                return products;
            }
        }
    }

    public Task<MaxioProduct?> ReadProductByHandleAsync(string handle, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<MaxioProductEnvelope, MaxioProduct>(
            $"products/handle/{Uri.EscapeDataString(handle)}.json",
            e => e.Product,
            cancellationToken);

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<MaxioCustomerEnvelope, MaxioCustomer>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            e => e.Customer,
            cancellationToken);

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new CreateCustomerRequest { Customer = customer },
            cancellationToken);

        return envelope?.Customer ?? throw new MaxioApiException(
            HttpStatusCode.OK,
            "POST",
            "customers.json",
            new[] { "Maxio reported success but returned no customer." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        // The spec declares no pagination parameters on this operation: Maxio returns all of the
        // customer's subscriptions in one response.
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

        List<MaxioSubscriptionEnvelope>? envelopes;
        try
        {
            envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The customer was removed in Maxio after we resolved it: no subscriptions, not an error.
            _logger.LogWarning("Maxio customer {CustomerId} no longer exists; reporting no subscriptions.", customerId);
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes ?? new List<MaxioSubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new CreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);

        return envelope?.Subscription ?? throw new MaxioApiException(
            HttpStatusCode.OK,
            "POST",
            "subscriptions.json",
            new[] { "Maxio reported success but returned no subscription." });
    }

    private async Task<TValue?> GetOrNullAsync<TEnvelope, TValue>(
        string path,
        Func<TEnvelope, TValue?> select,
        CancellationToken cancellationToken)
        where TEnvelope : class
        where TValue : class
    {
        try
        {
            var envelope = await SendAsync<TEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope is null ? null : select(envelope);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
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
        catch (OperationCanceledException ex)
        {
            // HttpClient surfaces its own timeout as a cancellation the caller did not ask for.
            throw new MaxioApiException(
                HttpStatusCode.GatewayTimeout,
                method.Method,
                path,
                new[] { $"The request to Maxio timed out after {_options.CurrentValue.TimeoutSeconds}s." },
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(
                HttpStatusCode.ServiceUnavailable,
                method.Method,
                path,
                new[] { "Could not reach the Maxio API: " + ex.Message },
                ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodySafelyAsync(response, cancellationToken);
                var errors = MaxioErrorParser.Parse(errorBody);

                _logger.LogWarning(
                    "Maxio {Method} {Path} responded {StatusCode}: {Errors}",
                    method.Method,
                    path,
                    (int)response.StatusCode,
                    errors.Count > 0 ? string.Join("; ", errors) : "(no error detail)");

                throw new MaxioApiException(response.StatusCode, method.Method, path, errors);
            }

            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            try
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (stream.ConfigureAwait(false))
                {
                    return await JsonSerializer.DeserializeAsync<TResponse>(stream, SerializerOptions, cancellationToken);
                }
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(
                    response.StatusCode,
                    method.Method,
                    path,
                    new[] { "Maxio returned a body that did not match the expected schema: " + ex.Message },
                    ex);
            }
        }
    }

    private static async Task<string?> ReadBodySafelyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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
}
