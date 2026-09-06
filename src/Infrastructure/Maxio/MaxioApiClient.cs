using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for Maxio Advanced Billing, written against the OpenAPI specification in
/// maxio-spec/. Authentication is HTTP Basic with the API key as the user name and "x" as the
/// password, exactly as the spec's BasicAuth security scheme describes.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const string BasicAuthPassword = "x";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
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

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var operation = MaxioOperations.ListProductsForProductFamily;
        var path = operation.PathTemplate.Replace(
            "{product_family_id}",
            Uri.EscapeDataString(productFamilyIdOrHandle),
            StringComparison.Ordinal);

        var query = includeArchived
            ? new Dictionary<string, string> { ["include_archived"] = "true" }
            : null;

        var envelopes = await SendAsync<List<MaxioProductResponse>>(
            operation.Method, path, query, content: null, allowNotFound: false, cancellationToken).ConfigureAwait(false);

        return Unwrap(envelopes, envelope => envelope.Product);
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var operation = MaxioOperations.ReadCustomerByReference;
        var query = new Dictionary<string, string> { ["reference"] = reference };

        var response = await SendAsync<MaxioCustomerResponse>(
            operation.Method, operation.PathTemplate, query, content: null, allowNotFound: true, cancellationToken).ConfigureAwait(false);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var operation = MaxioOperations.CreateCustomer;
        var request = new MaxioCreateCustomerRequest { Customer = customer };

        var response = await SendAsync<MaxioCustomerResponse>(
            operation.Method, operation.PathTemplate, query: null, content: request, allowNotFound: false, cancellationToken).ConfigureAwait(false);

        return response?.Customer
            ?? throw new BillingProviderException("Maxio returned an empty customer payload when creating a customer.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var operation = MaxioOperations.ListCustomerSubscriptions;
        var path = operation.PathTemplate.Replace(
            "{customer_id}",
            customerId.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        var envelopes = await SendAsync<List<MaxioSubscriptionResponse>>(
            operation.Method, path, query: null, content: null, allowNotFound: true, cancellationToken).ConfigureAwait(false);

        return Unwrap(envelopes, envelope => envelope.Subscription);
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default)
    {
        var operation = MaxioOperations.FindSubscription;
        var query = new Dictionary<string, string> { ["reference"] = reference };

        var response = await SendAsync<MaxioSubscriptionResponse>(
            operation.Method, operation.PathTemplate, query, content: null, allowNotFound: true, cancellationToken).ConfigureAwait(false);

        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var operation = MaxioOperations.CreateSubscription;
        var request = new MaxioCreateSubscriptionRequest { Subscription = subscription };

        var response = await SendAsync<MaxioSubscriptionResponse>(
            operation.Method, operation.PathTemplate, query: null, content: request, allowNotFound: false, cancellationToken).ConfigureAwait(false);

        return response?.Subscription
            ?? throw new BillingProviderException("Maxio returned an empty subscription payload when creating a subscription.");
    }

    private static IReadOnlyList<TItem> Unwrap<TEnvelope, TItem>(List<TEnvelope>? envelopes, Func<TEnvelope, TItem?> selector)
        where TItem : class
    {
        if (envelopes is null)
        {
            return Array.Empty<TItem>();
        }

        return envelopes
            .Select(selector)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string>? query,
        object? content,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var options = GetValidatedOptions();
        using var request = BuildRequest(options, method, path, query, content);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Maxio {Method} {Path} timed out after {ElapsedMs}ms.", method, path, stopwatch.ElapsedMilliseconds);
            throw new BillingProviderException($"The Maxio API did not respond within {options.TimeoutSeconds}s.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio {Method} {Path} could not be reached.", method, path);
            throw new BillingProviderException("The Maxio API could not be reached.", innerException: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs}ms.",
                method,
                path,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MaxioApiException(response.StatusCode, method.Method, path, MaxioErrorReader.Read(body));
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
                _logger.LogError(ex, "Maxio {Method} {Path} returned a payload that does not match the specification.", method, path);
                throw new BillingProviderException("The Maxio API returned an unreadable response.", innerException: ex);
            }
        }
    }

    private static HttpRequestMessage BuildRequest(
        MaxioOptions options,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string>? query,
        object? content)
    {
        var relative = path.TrimStart('/');
        if (query is { Count: > 0 })
        {
            relative += "?" + string.Join(
                "&",
                query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        }

        var request = new HttpRequestMessage(method, new Uri(options.ResolveBaseAddress(), relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:{BasicAuthPassword}")));

        if (content is not null)
        {
            request.Content = JsonContent.Create(content, content.GetType(), options: SerializerOptions);
        }

        return request;
    }

    private MaxioOptions GetValidatedOptions()
    {
        var options = _options.CurrentValue;
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new BillingConfigurationException("Maxio subscription billing is not configured.", errors);
        }

        return options;
    }
}
