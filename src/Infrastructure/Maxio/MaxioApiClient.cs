using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed HTTP client over the Maxio Advanced Billing REST API. Handles authentication,
/// base-address resolution, serialization and error mapping. Requests are built explicitly so the
/// client is thread-safe as a shared typed client and honors the verbatim <c>Maxio:BaseUrl</c>
/// override.
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    /// <summary>Lists the products (plans) belonging to a product family, identified by handle.</summary>
    internal async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        var envelopes = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                        ?? new List<ProductEnvelope>();

        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    /// <summary>Looks up a single customer by its unique reference. Returns null when none exists.</summary>
    internal async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return envelope?.Customer;
    }

    /// <summary>Creates a new customer.</summary>
    internal async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerEnvelope { Customer = customer };
        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        return envelope?.Customer
               ?? throw new MaxioApiException(500, "Maxio returned an empty customer on create.");
    }

    /// <summary>Lists the subscriptions belonging to a customer.</summary>
    internal async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json?per_page=200";
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                        ?? new List<SubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    /// <summary>Creates a new subscription.</summary>
    internal async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionEnvelope { Subscription = subscription };
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return envelope?.Subscription
               ?? throw new MaxioApiException(500, "Maxio returned an empty subscription on create.");
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        object? requestBody,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
        where TResponse : class
    {
        _settings.EnsureConfigured();

        var requestUri = new Uri(_settings.ResolveBaseAddress(), relativePath);
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _settings.BuildBasicAuthParameter());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (requestBody is not null)
        {
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to reach Maxio for {Method} {Path}.", method, relativePath);
            throw new MaxioApiException(503, "Unable to reach the billing provider (Maxio).", innerException: ex);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await SafeReadBodyAsync(response, cancellationToken);
                _logger.LogWarning("Maxio {Method} {Path} returned {Status}: {Body}",
                    method, relativePath, (int)response.StatusCode, errorBody);
                throw new MaxioApiException(
                    (int)response.StatusCode,
                    $"Maxio request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    errorBody);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize Maxio response for {Method} {Path}.", method, relativePath);
                throw new MaxioApiException(502, "Received an unexpected response from the billing provider (Maxio).", innerException: ex);
            }
        }
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
