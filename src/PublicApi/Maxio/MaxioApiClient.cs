using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API (JSON endpoints).
/// Plain HTTP is used deliberately: the Advanced Billing contract is small for the
/// capabilities this integration needs (catalog, customers, subscriptions) and the
/// endpoints/fields below are verified against the official API documentation and the
/// Maxio sandbox.
/// </summary>
public class MaxioApiClient
{
    private const string JsonMediaType = "application/json";
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(1) };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, MaxioOptions options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl) and " +
                "Maxio:ProductFamilyHandle via user-secrets or the MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / " +
                "MAXIO_BASE_URL / MAXIO_DEFAULT_PRODUCT_FAMILY environment variables.");
        }
    }

    /// <summary>Lists all products (plans) on the site, unfiltered.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var envelopes = await GetJsonAsync<List<ProductEnvelope>>("products.json", cancellationToken);
        return envelopes?
            .Where(e => e.Product != null)
            .Select(e => e.Product!)
            .ToList() ?? new List<MaxioProduct>();
    }

    /// <summary>
    /// Finds a single customer by the reference value supplied by this application.
    /// Returns null when no customer carries that reference.
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var envelope = await GetJsonAsync<CustomerEnvelope>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken, allowNotFound: true);
        return envelope?.Customer;
    }

    /// <summary>Creates a customer. Throws <see cref="MaxioApiException"/> on validation errors (e.g. duplicate reference).</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft draft, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var envelope = await SendJsonAsync<CustomerEnvelope>(
            HttpMethod.Post, "customers.json", new CreateCustomerBody { Customer = draft }, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(201, "Maxio created a customer but returned an empty response body.");
    }

    /// <summary>Lists every subscription that belongs to the given Maxio customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var envelopes = await GetJsonAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return envelopes?
            .Where(e => e.Subscription != null)
            .Select(e => e.Subscription!)
            .ToList() ?? new List<MaxioSubscription>();
    }

    /// <summary>
    /// Creates a subscription for an existing customer (matched by reference) to a product (matched by handle).
    /// No payment profile is supplied; the plan is subscribed with remittance (invoice) collection so that
    /// subscribing never requires card capture / 3-DS.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionDraft draft, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var envelope = await SendJsonAsync<SubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", new CreateSubscriptionBody { Subscription = draft }, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(201, "Maxio created a subscription but returned an empty response body.");
    }

    private async Task<T?> GetJsonAsync<T>(string requestUri, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return Deserialize<T>(body);
                }

                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return default;
                }

                throw new MaxioApiException((int)response.StatusCode, $"Maxio GET {requestUri} failed.", ExtractErrors(body));
            }
            catch (MaxioApiException apiException) when (apiException.IsUpstreamError && attempt < RetryDelays.Length && ShouldRetry(cancellationToken))
            {
                _logger.LogWarning("Transient Maxio status {Status} on GET {RequestUri} (attempt {Attempt}/{MaxAttempts}); retrying.",
                    apiException.StatusCode, requestUri, attempt + 1, RetryDelays.Length);
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < RetryDelays.Length && ShouldRetry(cancellationToken))
            {
                _logger.LogWarning(ex, "Transient failure talking to Maxio (attempt {Attempt}/{MaxAttempts}); retrying.", attempt + 1, RetryDelays.Length);
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
            catch (MaxioApiException)
            {
                throw;
            }
        }
    }

    private async Task<T?> SendJsonAsync<T>(HttpMethod method, string requestUri, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerialize(body);
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, JsonMediaType)
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new MaxioApiException($"Maxio {method} {requestUri} could not be completed.", ex);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return Deserialize<T>(responseBody);
        }

        throw new MaxioApiException((int)response.StatusCode, $"Maxio {method} {requestUri} failed.", ExtractErrors(responseBody));
    }

    private static bool ShouldRetry(CancellationToken cancellationToken) => !cancellationToken.IsCancellationRequested;

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or OperationCanceledException;

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(json, MaxioJson.Options);
    }

    private static string JsonSerialize(object value) =>
        System.Text.Json.JsonSerializer.Serialize(value, MaxioJson.Options);

    private static IReadOnlyList<string>? ExtractErrors(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<MaxioErrorsBody>(responseBody, MaxioJson.Options);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not the {"errors": [...]} shape; fall through.
        }

        return null;
    }
}
