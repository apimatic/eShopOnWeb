using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API, built directly against
/// <c>maxio-spec/openapi.yaml</c>.
/// </summary>
/// <remarks>
/// Paths, query parameters, request and response bodies and the authentication scheme all come from
/// that specification; see <see cref="IMaxioApiClient"/> for the operation ids. Failures are
/// translated into <see cref="BillingProviderException"/> so that callers never have to reason
/// about <see cref="HttpResponseMessage"/> or about provider payload shapes.
/// </remarks>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Largest page the specification allows for paginated list operations.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guard against an unbounded loop if the provider ever stops honouring pagination.</summary>
    private const int MaxPages = 50;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<MaxioSiteResponse>("site.json", cancellationToken);
        return response?.Site
            ?? throw new BillingProviderException("Maxio returned an empty site record.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productFamilyIdOrHandle);

        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json"
                       + $"?page={page}&per_page={MaxPageSize}";

            var envelopes = await GetAsync<List<MaxioProductResponse>>(path, cancellationToken);
            if (envelopes is null || envelopes.Count == 0)
            {
                break;
            }

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < MaxPageSize)
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

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioCustomerResponse>(path, cancellationToken, notFoundIsNull: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var payload = new MaxioCreateCustomerRequest { Customer = customer };
        var response = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            "customers.json", payload, cancellationToken);

        return response?.Customer
            ?? throw new BillingProviderException("Maxio accepted the customer but returned no customer record.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await GetAsync<List<MaxioSubscriptionResponse>>(path, cancellationToken, notFoundIsNull: true);

        if (envelopes is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

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

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioSubscriptionResponse>(path, cancellationToken, notFoundIsNull: true);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var payload = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var response = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "subscriptions.json", payload, cancellationToken);

        return response?.Subscription
            ?? throw new BillingProviderException("Maxio accepted the signup but returned no subscription record.");
    }

    private Task<TResponse?> GetAsync<TResponse>(
        string path,
        CancellationToken cancellationToken,
        bool notFoundIsNull = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        return SendAsync<TResponse>(request, notFoundIsNull, cancellationToken);
    }

    private Task<TResponse?> PostAsync<TRequest, TResponse>(
        string path,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: MaxioJson.Options)
        };

        return SendAsync<TResponse>(request, notFoundIsNull: false, cancellationToken);
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpRequestMessage request,
        bool notFoundIsNull,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "Maxio {Method} {Path} timed out after {Elapsed} ms.",
                request.Method, SafePath(request), stopwatch.ElapsedMilliseconds);

            throw new BillingProviderException(
                "The billing provider did not respond in time.", providerStatusCode: 504);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Maxio {Method} {Path} could not be reached after {Elapsed} ms.",
                request.Method, SafePath(request), stopwatch.ElapsedMilliseconds);

            throw new BillingProviderException(
                "The billing provider could not be reached.", innerException: ex);
        }

        using (response)
        {
            _logger.LogInformation(
                "Maxio {Method} {Path} responded {StatusCode} in {Elapsed} ms.",
                request.Method, SafePath(request), (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await BuildFailureAsync(request, response, cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(MaxioJson.Options, cancellationToken);
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "Maxio {Method} {Path} returned a body that does not match the specification.",
                    request.Method, SafePath(request));

                throw new BillingProviderException(
                    "The billing provider returned an unreadable response.",
                    providerStatusCode: (int)response.StatusCode,
                    innerException: ex);
            }
        }
    }

    private async Task<BillingProviderException> BuildFailureAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        var errors = MaxioErrorReader.Read(body);
        var status = (int)response.StatusCode;

        var summary = status switch
        {
            401 or 403 => "The billing provider rejected the configured API credentials.",
            404 => "The billing provider does not know the requested resource.",
            422 => "The billing provider rejected the request.",
            429 => "The billing provider is rate limiting this application.",
            >= 500 => "The billing provider reported an internal error.",
            _ => "The billing provider rejected the request."
        };

        // Credential failures are logged without the response body: it is the one payload that can
        // echo request material back, and nothing about it helps an operator more than the status.
        if (status is 401 or 403)
        {
            _logger.LogError(
                "Maxio {Method} {Path} was refused with {StatusCode}. Check the Maxio:ApiKey and Maxio:Subdomain settings.",
                request.Method, SafePath(request), status);
        }
        else
        {
            _logger.LogError(
                "Maxio {Method} {Path} failed with {StatusCode}: {Errors}",
                request.Method, SafePath(request), status, string.Join("; ", errors));
        }

        return new BillingProviderException(summary, status, errors);
    }

    /// <summary>
    /// Renders the request path for logs without its query string, which can carry customer
    /// references and e-mail addresses.
    /// </summary>
    private static string SafePath(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null)
        {
            return "(unknown)";
        }

        return uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?')[0];
    }
}
