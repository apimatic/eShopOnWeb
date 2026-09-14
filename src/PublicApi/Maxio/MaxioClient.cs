using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing REST API.
///
/// Every endpoint/field used here was confirmed against the current Maxio
/// Advanced Billing API (https://developers.maxio.com) and verified against the
/// sandbox site during development. Authentication is HTTP Basic with the API
/// key as the username and "x" as the password (Maxio documented convention).
/// </summary>
public sealed class MaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        if (!_options.Value.IsConfigured)
        {
            // Requests are guarded in SendAsync with a clear error; keep construction safe so the
            // rest of the app (one-time commerce) still works without billing being configured.
            return;
        }

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = _options.Value.BaseUri;
        }

        var credentials = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"{_options.Value.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>GET the non-archived products (plans) that belong to the product family with the given handle.</summary>
    public async Task<IReadOnlyList<MaxioProductDto>> ListProductsByFamilyHandleAsync(
        string familyHandle,
        CancellationToken cancellationToken)
    {
        var requestUri = $"product_families/{EscapeSegment($"handle:{familyHandle}")}/products.json?include_archived=false";
        var body = await SendAsync(HttpMethod.Get, requestUri, null, cancellationToken).ConfigureAwait(false);
        var envelopes = Deserialize<List<MaxioProductEnvelope>>(body, requestUri);

        var products = new List<MaxioProductDto>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }
        return products;
    }

    /// <summary>Returns the customer matching <paramref name="reference"/> or null when no such customer exists.</summary>
    public async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var requestUri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var body = await SendAsync(HttpMethod.Get, requestUri, null, cancellationToken).ConfigureAwait(false);
            var envelope = Deserialize<MaxioCustomerEnvelope>(body, requestUri);
            return envelope.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Creates a customer. Throws on a duplicate reference (HTTP 422).</summary>
    public async Task<MaxioCustomerDto> CreateCustomerAsync(
        MaxioCreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var requestUri = "customers.json";
        var body = await SendAsync(HttpMethod.Post, requestUri, request, cancellationToken).ConfigureAwait(false);
        var envelope = Deserialize<MaxioCustomerEnvelope>(body, requestUri);
        return envelope.Customer ?? throw InvalidResponse(requestUri, body);
    }

    /// <summary>Creates a subscription. Returns the created subscription (HTTP 201).</summary>
    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(
        MaxioCreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var requestUri = "subscriptions.json";
        var body = await SendAsync(HttpMethod.Post, requestUri, request, cancellationToken).ConfigureAwait(false);
        var envelope = Deserialize<MaxioSubscriptionEnvelope>(body, requestUri);
        return envelope.Subscription ?? throw InvalidResponse(requestUri, body);
    }

    /// <summary>Lists the subscriptions that belong to the customer with <paramref name="customerId"/>.</summary>
    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var requestUri = $"customers/{customerId}/subscriptions.json?per_page=200";
        var body = await SendAsync(HttpMethod.Get, requestUri, null, cancellationToken).ConfigureAwait(false);
        var envelopes = Deserialize<List<MaxioSubscriptionEnvelope>>(body, requestUri);

        var subscriptions = new List<MaxioSubscriptionDto>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }
        return subscriptions;
    }

    /// <summary>Returns the site profile (used to resolve the site currency).</summary>
    public async Task<MaxioSiteDto> GetSiteAsync(CancellationToken cancellationToken)
    {
        const string requestUri = "site.json";
        var body = await SendAsync(HttpMethod.Get, requestUri, null, cancellationToken).ConfigureAwait(false);
        var envelope = Deserialize<MaxioSiteEnvelope>(body, requestUri);
        return envelope.Site ?? throw InvalidResponse(requestUri, body);
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string requestUri,
        object? payload,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(method, requestUri);
        if (payload is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioUnavailableException($"Maxio Advanced Billing API {method} {requestUri} timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioUnavailableException(
                $"Maxio Advanced Billing API {method} {requestUri} could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = Truncate(responseBody, 500);
                _logger.LogWarning("Maxio API call {Method} {Uri} failed with {StatusCode}: {Detail}",
                    method, requestUri, (int)response.StatusCode, detail);
                throw new MaxioApiException(
                    response.StatusCode,
                    $"Maxio Advanced Billing API {method} {requestUri} returned {(int)response.StatusCode} {response.ReasonPhrase}. {detail}",
                    responseBody);
            }

            return responseBody;
        }
    }

    private static T Deserialize<T>(string body, string requestUri)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
            if (result is null)
            {
                throw InvalidResponse(requestUri, body);
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                HttpStatusCode.OK,
                $"Maxio Advanced Billing API {requestUri} returned an unparseable payload: {ex.Message}",
                Truncate(body, 500));
        }
    }

    private static MaxioApiException InvalidResponse(string requestUri, string body) =>
        new(HttpStatusCode.OK,
            $"Maxio Advanced Billing API {requestUri} returned an unexpected empty payload.",
            Truncate(body, 500));

    private static string EscapeSegment(string segment) => Uri.EscapeDataString(segment);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private void EnsureConfigured()
    {
        var options = _options.Value;
        if (!options.IsConfigured)
        {
            throw new MaxioUnavailableException(
                "Maxio Advanced Billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain and " +
                "Maxio:ProductFamilyHandle (via the MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and " +
                "MAXIO_DEFAULT_PRODUCT_FAMILY environment variables or .NET user-secrets).");
        }
    }
}
