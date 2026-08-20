using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _http;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient http,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        EnsureHttpClient();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        int page,
        int perPage,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        // GET /product_families/{product_family_id}/products.json
        // product_family_id: "Either the product family's id or its handle prefixed with `handle:`"
        var path =
            $"product_families/{productFamilyIdOrHandle}/products.json?page={page}&per_page={perPage}&include_archived={includeArchived.ToString().ToLowerInvariant()}";
        var wrappers = await GetJsonAsync<List<MaxioProductResponse>>(path, cancellationToken);
        return wrappers?.Select(w => w.Product).Where(p => p is not null).Cast<MaxioProduct>().ToList()
               ?? new List<MaxioProduct>();
    }

    public async Task<MaxioProduct?> ReadProductByHandleAsync(
        string apiHandle,
        CancellationToken cancellationToken = default)
    {
        // GET /products/handle/{api_handle}.json
        var path = $"products/handle/{Uri.EscapeDataString(apiHandle)}.json";
        var wrapper = await GetJsonOrNotFoundAsync<MaxioProductResponse>(path, cancellationToken);
        return wrapper?.Product;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await GetJsonOrNotFoundAsync<MaxioCustomerResponse>(path, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        // POST /customers.json
        var wrapper = await PostJsonAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            "customers.json", request, cancellationToken, HttpStatusCode.OK);
        return Require(wrapper.Customer, "createCustomer response was missing customer");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await GetJsonAsync<List<MaxioSubscriptionResponse>>(path, cancellationToken);
        return wrappers?.Select(w => w.Subscription).Where(s => s is not null).Cast<MaxioSubscription>().ToList()
               ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /subscriptions/lookup.json?reference=
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await GetJsonOrNotFoundAsync<MaxioSubscriptionResponse>(path, cancellationToken);
        return wrapper?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        // POST /subscriptions.json — 201 Created
        var wrapper = await PostJsonAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "subscriptions.json", request, cancellationToken, HttpStatusCode.Created, HttpStatusCode.OK);
        return Require(wrapper.Subscription, "createSubscription response was missing subscription");
    }

    private void EnsureHttpClient()
    {
        if (_http.BaseAddress is null && _options.IsConfigured)
        {
            _http.BaseAddress = new Uri(_options.ResolveApiBaseUrl());
        }

        _http.Timeout = TimeSpan.FromSeconds(30);
        if (!_http.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
        {
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private AuthenticationHeaderValue CreateBasicAuth()
    {
        _options.EnsureConfigured();
        // Spec securitySchemes.BasicAuth: username is the API key, password is `x`.
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private async Task<T?> GetJsonOrNotFoundAsync<T>(string relativeUrl, CancellationToken cancellationToken)
        where T : class
    {
        using var request = CreateRequest(HttpMethod.Get, relativeUrl);
        using var response = await SendWithRetryAsync(request, idempotent: true, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetJsonAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, relativeUrl);
        using var response = await SendWithRetryAsync(request, idempotent: true, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string relativeUrl,
        TRequest body,
        CancellationToken cancellationToken,
        params HttpStatusCode[] expected)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
        using var request = CreateRequest(HttpMethod.Post, relativeUrl);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendWithRetryAsync(request, idempotent: false, cancellationToken);
        if (expected.Length > 0 && !expected.Contains(response.StatusCode))
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
        else
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }

        return Require(await ReadJsonAsync<TResponse>(response, cancellationToken),
            $"Maxio returned an empty body for POST {relativeUrl}");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        _options.EnsureConfigured();
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(_options.ResolveApiBaseUrl());
        }

        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = CreateBasicAuth();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        bool idempotent,
        CancellationToken cancellationToken)
    {
        var maxAttempts = idempotent ? 3 : 1;
        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            HttpRequestMessage attemptRequest;
            if (attempt == 1)
            {
                attemptRequest = request;
            }
            else
            {
                attemptRequest = await CloneRequestAsync(request);
            }

            _logger.LogInformation("Maxio {Method} {Path} (attempt {Attempt})",
                attemptRequest.Method, attemptRequest.RequestUri, attempt);

            try
            {
                response = await _http.SendAsync(attemptRequest, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Transient failure calling Maxio; retrying.");
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                continue;
            }

            var status = (int)response.StatusCode;
            if (attempt < maxAttempts && (status == 429 || status >= 500))
            {
                var delay = TimeSpan.FromMilliseconds(300 * attempt);
                if (response.Headers.RetryAfter?.Delta is { } retryAfter)
                {
                    delay = retryAfter;
                }

                _logger.LogWarning("Maxio returned {Status}; retrying after {Delay}.", status, delay);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
        }

        return response!;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = ExtractErrorDetail(body);
        var path = response.RequestMessage?.RequestUri?.PathAndQuery ?? string.Empty;
        throw new MaxioApiException(
            response.StatusCode,
            $"Maxio API {path} returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}",
            body);
    }

    private static string ExtractErrorDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var messages = errors.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    return string.Join(" ", messages);
                }

                return errors.ToString();
            }
        }
        catch (JsonException)
        {
            // fall through — return a truncated raw body
        }

        return body.Length <= 500 ? body : body[..500];
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, MaxioJson.SerializerOptions);
    }

    private static T Require<T>(T? value, string message)
    {
        if (value is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, message);
        }

        return value;
    }
}
