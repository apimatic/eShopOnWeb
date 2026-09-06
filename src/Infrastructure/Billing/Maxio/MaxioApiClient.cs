using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing API over HTTP, exactly as described by the OpenAPI
/// specification in <c>maxio-spec/</c>: paths, query parameters, request and response schemas and
/// error models all come from there. Authentication and retries are applied by delegating handlers
/// configured alongside the underlying <see cref="HttpClient"/>.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Cap on how much of an error body is kept for diagnostics.</summary>
    private const int MaxErrorBodyLength = 4096;

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptionsMonitor<MaxioOptions> options,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle, int page, int perPage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productFamilyIdOrHandle);

        var path = $"/product_families/{EscapeProductFamily(productFamilyIdOrHandle)}/products.json" +
                   $"?page={page.ToString(CultureInfo.InvariantCulture)}" +
                   $"&per_page={perPage.ToString(CultureInfo.InvariantCulture)}";

        var envelopes = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, content: null,
            cancellationToken) ?? new List<ProductResponse>();

        return envelopes.Select(e => e.Product).Where(p => p is not null).Select(p => p!).ToList();
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        // The specification documents only a 200 for this operation; Maxio answers 404 when no
        // customer carries the reference, which is an expected outcome rather than a failure.
        var response = await SendAsync<CustomerResponse>(HttpMethod.Get, path, content: null,
            cancellationToken, notFoundIsNull: true);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "/customers.json",
            JsonContent.Create(request, options: MaxioJson.Options), cancellationToken);

        return response?.Customer
               ?? throw new MaxioApiException("Maxio returned no customer when creating a customer.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

        var envelopes = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, content: null,
            cancellationToken) ?? new List<SubscriptionResponse>();

        return envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "/subscriptions.json",
            JsonContent.Create(request, options: MaxioJson.Options), cancellationToken);

        return response?.Subscription
               ?? throw new MaxioApiException("Maxio returned no subscription when creating a subscription.");
    }

    public async Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<SiteResponse>(HttpMethod.Get, "/site.json", content: null,
            cancellationToken);

        return response?.Site;
    }

    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path,
        HttpContent? content, CancellationToken cancellationToken, bool notFoundIsNull = false)
        where TResponse : class
    {
        var options = _options.CurrentValue;
        var configurationErrors = options.Validate();
        if (configurationErrors.Count > 0)
        {
            throw new BillingConfigurationException(
                "Maxio subscription billing is not configured: " + string.Join(" ", configurationErrors));
        }

        using var request = new HttpRequestMessage(method, options.ResolveBaseUrl() + path);
        request.Content = content;

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new MaxioApiException($"The Maxio request '{method.Method} {PathOnly(path)}' timed out.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(
                $"The Maxio request '{method.Method} {PathOnly(path)}' failed: {ex.Message}",
                innerException: ex);
        }

        using (response)
        {
            stopwatch.Stop();
            _logger.LogDebug("Maxio {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms.",
                method.Method, PathOnly(path), (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(method, path, response, cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.NoContent ||
                response.Content.Headers.ContentLength == 0)
            {
                return null;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(MaxioJson.Options,
                    cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(
                    $"The Maxio response to '{method.Method} {PathOnly(path)}' could not be read: {ex.Message}",
                    (int)response.StatusCode, innerException: ex);
            }
        }
    }

    private async Task<Exception> CreateExceptionAsync(HttpMethod method, string path,
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadErrorBodyAsync(response, cancellationToken);
        var messages = MaxioJson.ReadErrorMessages(body);
        var status = (int)response.StatusCode;
        var detail = messages.Count > 0 ? string.Join(" ", messages) : response.ReasonPhrase;

        var message = $"Maxio answered '{method.Method} {PathOnly(path)}' with {status}" +
                      (string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}");

        _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}: {Detail}",
            method.Method, PathOnly(path), status, detail);

        // 422 is how the specification models a rejected payload; the caller has to change the
        // request for it to succeed, so it is surfaced as a validation problem rather than an
        // upstream fault.
        return status == (int)HttpStatusCode.UnprocessableEntity
            ? new MaxioValidationException(message, status, messages)
            : new MaxioApiException(message, status, messages);
    }

    private static async Task<string?> ReadErrorBodyAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length > MaxErrorBodyLength ? body.Substring(0, MaxErrorBodyLength) : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Escapes the <c>product_family_id</c> path value while preserving the <c>handle:</c> prefix
    /// the specification defines for that parameter.
    /// </summary>
    private static string EscapeProductFamily(string productFamilyIdOrHandle)
    {
        const string prefix = "handle:";

        return productFamilyIdOrHandle.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? prefix + Uri.EscapeDataString(productFamilyIdOrHandle.Substring(prefix.Length))
            : Uri.EscapeDataString(productFamilyIdOrHandle);
    }

    /// <summary>Drops the query string, which can carry customer identifiers, before logging.</summary>
    private static string PathOnly(string path)
    {
        var index = path.IndexOf('?');
        return index < 0 ? path : path.Substring(0, index);
    }
}
