using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// A hand-written client for the Maxio Advanced Billing REST API, built directly against the
/// OpenAPI specification in <c>maxio-spec/</c>. Paths, parameters, payload shapes, the HTTP Basic
/// auth scheme and the server templating all come from that document.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    /// <summary>Password half of the Basic credential, fixed by the spec's <c>BasicAuth</c> scheme.</summary>
    private const string BasicAuthPassword = "x";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;
    private readonly string _baseAddress;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var settings = options.Value;
        var configurationErrors = settings.Validate();
        if (configurationErrors.Count > 0)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured: " + string.Join(" ", configurationErrors));
        }

        _baseAddress = settings.ResolveBaseAddress().TrimEnd('/');
        _httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:{BasicAuthPassword}")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
    }

    public Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MaxioSiteResponse, MaxioSite>("readSite", "/site.json", r => r.Site, cancellationToken);

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle, int page, int perPage, CancellationToken cancellationToken = default)
    {
        var path = $"/product_families/{EscapePathSegment(productFamilyIdOrHandle)}/products.json" +
                   $"?page={page.ToString(CultureInfo.InvariantCulture)}" +
                   $"&per_page={perPage.ToString(CultureInfo.InvariantCulture)}";

        // The operation answers with an array of Product-Response envelopes. A 404 here means the
        // product family does not exist, which is a configuration fault rather than an empty list.
        var envelopes = await GetAsync<List<MaxioProductResponse>>(
            "listProductsForProductFamily", path, treatNotFoundAsNull: false, cancellationToken).ConfigureAwait(false);

        return envelopes is null
            ? Array.Empty<MaxioProduct>()
            : envelopes.Select(e => e.Product).Where(p => p is not null).Select(p => p!).ToList();
    }

    public Task<MaxioProduct?> ReadProductByHandleAsync(string apiHandle, CancellationToken cancellationToken = default) =>
        GetAsync<MaxioProductResponse, MaxioProduct>(
            "readProductByHandle",
            $"/products/handle/{EscapePathSegment(apiHandle)}.json",
            r => r.Product,
            cancellationToken);

    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        GetAsync<MaxioCustomerResponse, MaxioCustomer>(
            "readCustomerByReference",
            $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            r => r.Customer,
            cancellationToken);

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            "createCustomer", "/customers.json", request, cancellationToken).ConfigureAwait(false);

        return response?.Customer ?? throw new MaxioApiException(
            "createCustomer", HttpStatusCode.OK, new[] { "The response did not contain a customer." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"/customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

        var envelopes = await GetAsync<List<MaxioSubscriptionResponse>>(
            "listCustomerSubscriptions", path, treatNotFoundAsNull: false, cancellationToken).ConfigureAwait(false);

        return envelopes is null
            ? Array.Empty<MaxioSubscription>()
            : envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default) =>
        GetAsync<MaxioSubscriptionResponse, MaxioSubscription>(
            "findSubscription",
            $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            r => r.Subscription,
            cancellationToken);

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "createSubscription", "/subscriptions.json", request, cancellationToken).ConfigureAwait(false);

        return response?.Subscription ?? throw new MaxioApiException(
            "createSubscription", HttpStatusCode.Created, new[] { "The response did not contain a subscription." });
    }

    /// <summary>Issues a GET whose 404 means "no such resource" and is surfaced as <c>null</c>.</summary>
    private async Task<TResult?> GetAsync<TEnvelope, TResult>(
        string operationId,
        string path,
        Func<TEnvelope, TResult?> unwrap,
        CancellationToken cancellationToken)
        where TEnvelope : class
        where TResult : class
    {
        var envelope = await GetAsync<TEnvelope>(operationId, path, treatNotFoundAsNull: true, cancellationToken)
            .ConfigureAwait(false);

        return envelope is null ? null : unwrap(envelope);
    }

    private async Task<T?> GetAsync<T>(
        string operationId, string path, bool treatNotFoundAsNull, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseAddress + path);
        return await SendAsync<T>(operationId, request, treatNotFoundAsNull, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string operationId, string path, TRequest payload, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseAddress + path)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };

        return await SendAsync<TResponse>(operationId, request, treatNotFoundAsNull: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<T?> SendAsync<T>(
        string operationId, HttpRequestMessage request, bool treatNotFoundAsNull, CancellationToken cancellationToken)
        where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient surfaces its own timeout as a cancellation that the caller did not request.
            _logger.LogWarning(ex, "Maxio operation {OperationId} timed out.", operationId);
            throw new MaxioTransportException(operationId, $"Maxio operation '{operationId}' timed out.", ex)
            {
                IsTimeout = true
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Maxio operation {OperationId} could not reach the API.", operationId);
            throw new MaxioTransportException(operationId, $"Maxio operation '{operationId}' could not reach the API.", ex);
        }

        using (response)
        {
            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Maxio operation {OperationId} returned 404; treating as no result.", operationId);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                var errors = MaxioErrorReader.Read(errorBody);

                _logger.LogWarning(
                    "Maxio operation {OperationId} failed with status {StatusCode}: {Errors}",
                    operationId, (int)response.StatusCode, string.Join("; ", errors));

                throw new MaxioApiException(operationId, response.StatusCode, errors);
            }

            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Maxio operation {OperationId} returned a body that could not be parsed.", operationId);
                throw new MaxioApiException(
                    operationId,
                    response.StatusCode,
                    new[] { "The response body did not match the expected schema." },
                    ex);
            }
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Escapes a value for use inside a path segment while preserving the <c>handle:</c> prefix the
    /// specification defines for product family lookups.
    /// </summary>
    private static string EscapePathSegment(string value)
    {
        const string handlePrefix = "handle:";

        return value.StartsWith(handlePrefix, StringComparison.OrdinalIgnoreCase)
            ? handlePrefix + Uri.EscapeDataString(value[handlePrefix.Length..])
            : Uri.EscapeDataString(value);
    }
}
