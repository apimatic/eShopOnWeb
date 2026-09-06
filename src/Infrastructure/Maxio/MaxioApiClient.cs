using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioApiClient"/> implemented against <c>maxio-spec/openapi.yaml</c>: the server
/// template supplies the base address, the <c>BasicAuth</c> scheme supplies the credentials (API
/// key as the username, literal <c>x</c> as the password) and the documented paths, parameters and
/// schemas supply every request and response shape.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Largest page the spec allows for list operations.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Safety stop so a misbehaving upstream cannot spin the pager forever.</summary>
    private const int MaxPages = 50;

    /// <summary>How much of a failed response body is kept for diagnostics.</summary>
    private const int MaxCapturedBodyLength = 2000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioSiteResponse>(HttpMethod.Get, "/site.json", null, cancellationToken);
        return response?.Site ?? throw new MaxioApiException(
            HttpStatusCode.OK, "GET", "/site.json", new[] { "Response did not contain a site." }, null);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyIdOrHandle))
        {
            throw new ArgumentException("A product family id or handle is required.", nameof(productFamilyIdOrHandle));
        }

        var basePath = $"/product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json";
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var query = new Dictionary<string, string?>
            {
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["per_page"] = MaxPageSize.ToString(CultureInfo.InvariantCulture),
                ["include_archived"] = includeArchived ? "true" : "false"
            };

            var pageResults = await SendAsync<List<MaxioProductResponse>>(
                HttpMethod.Get, basePath, query, cancellationToken);

            if (pageResults is null || pageResults.Count == 0)
            {
                break;
            }

            foreach (var item in pageResults)
            {
                if (item.Product is not null)
                {
                    products.Add(item.Product);
                }
            }

            if (pageResults.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var query = new Dictionary<string, string?> { ["reference"] = reference };
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get, "/customers/lookup.json", query, cancellationToken, treatNotFoundAsNull: true);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioCustomerResponse, MaxioCreateCustomerRequest>(
            HttpMethod.Post, "/customers.json", null, request, cancellationToken);

        return response?.Customer ?? throw new MaxioApiException(
            HttpStatusCode.OK, "POST", "/customers.json", new[] { "Response did not contain a customer." }, null);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"/customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var results = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        if (results is null)
        {
            return subscriptions;
        }

        foreach (var item in results)
        {
            if (item.Subscription is not null)
            {
                subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A subscription reference is required.", nameof(reference));
        }

        var query = new Dictionary<string, string?> { ["reference"] = reference };
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get, "/subscriptions/lookup.json", query, cancellationToken, treatNotFoundAsNull: true);

        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioSubscriptionResponse, MaxioCreateSubscriptionRequest>(
            HttpMethod.Post, "/subscriptions.json", null, request, cancellationToken);

        return response?.Subscription ?? throw new MaxioApiException(
            HttpStatusCode.OK, "POST", "/subscriptions.json", new[] { "Response did not contain a subscription." }, null);
    }

    private Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        IDictionary<string, string?>? query,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
        where TResponse : class
        => SendAsync<TResponse, object>(method, path, query, null, cancellationToken, treatNotFoundAsNull);

    private async Task<TResponse?> SendAsync<TResponse, TRequest>(
        HttpMethod method,
        string path,
        IDictionary<string, string?>? query,
        TRequest? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
        where TResponse : class
        where TRequest : class
    {
        var requestUri = BuildRequestUri(path, query);

        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = BuildAuthorizationHeader();

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Maxio API call {Method} {Path} timed out after {Timeout}s.", method.Method, path, _settings.TimeoutSeconds);
            throw new MaxioTransportException($"Maxio API call {method.Method} {path} timed out.", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio API call {Method} {Path} could not be completed.", method.Method, path);
            throw new MaxioTransportException($"Maxio API call {method.Method} {path} could not be completed.", ex);
        }

        using (response)
        {
            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Maxio API call {Method} {Path} returned 404; treating as no result.", method.Method, path);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodyAsync(response, cancellationToken);
                var errors = MaxioErrorParser.Parse(errorBody);
                _logger.LogError(
                    "Maxio API call {Method} {Path} failed with status {StatusCode}. Errors: {Errors}",
                    method.Method, path, (int)response.StatusCode, string.Join(" | ", errors));

                throw new MaxioApiException(response.StatusCode, method.Method, path, errors, Truncate(errorBody));
            }

            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return null;
            }

            try
            {
                var result = await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
                _logger.LogDebug("Maxio API call {Method} {Path} succeeded with status {StatusCode}.", method.Method, path, (int)response.StatusCode);
                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Maxio API call {Method} {Path} returned a body that does not match the spec.", method.Method, path);
                throw new MaxioApiException(
                    response.StatusCode, method.Method, path,
                    new[] { "The response body did not match the expected schema." }, null);
            }
        }
    }

    private Uri BuildRequestUri(string path, IDictionary<string, string?>? query)
    {
        var builder = new StringBuilder(_settings.ResolveBaseAddress());
        builder.Append(path);

        if (query is not null)
        {
            var first = true;
            foreach (var pair in query)
            {
                if (pair.Value is null)
                {
                    continue;
                }

                builder.Append(first ? '?' : '&');
                first = false;
                builder.Append(Uri.EscapeDataString(pair.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(pair.Value));
            }
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    /// <summary>
    /// HTTP Basic credentials per the <c>BasicAuth</c> security scheme in the spec: the API key is
    /// the username and the password is the literal <c>x</c>.
    /// </summary>
    private AuthenticationHeaderValue BuildAuthorizationHeader()
    {
        var apiKey = _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"{MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.ApiKey)} is not configured.");
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:x"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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

    private static string? Truncate(string? value) =>
        value is null || value.Length <= MaxCapturedBodyLength ? value : value[..MaxCapturedBodyLength];
}
