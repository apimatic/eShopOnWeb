using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioClient"/> against the Maxio Advanced
/// Billing API. Every request/response shape mirrors maxio-spec/openapi.yaml:
/// basic auth with the API key as username and "x" as password, JSON envelopes
/// ({ "product": ... } etc.) and snake_case fields.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>Page size for paginated list operations (within the documented maximum).</summary>
    private const int PerPage = 100;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the API base address. When <see cref="MaxioOptions.BaseUrl"/> is
    /// configured it is used verbatim; otherwise the address is derived from the
    /// site subdomain, per the server templating in the OpenAPI spec
    /// (US: https://{site}.chargify.com).
    /// </summary>
    public static Uri ResolveBaseUrl(MaxioOptions options)
    {
        var raw = !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? options.BaseUrl.Trim()
            : $"https://{options.Subdomain.Trim()}.chargify.com";
        return new Uri(raw.TrimEnd('/') + "/");
    }

    /// <summary>Builds the basic-auth header value ("{api_key}:x") per the spec's auth scheme.</summary>
    public static AuthenticationHeaderValue BuildAuthHeader(MaxioOptions options) =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x")));

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var responses = await GetAsync<List<MaxioProductResponse>>($"products.json?page={page}&per_page={PerPage}",
                HttpRequestMethod.Get, cancellationToken);
            products.AddRange(responses.Where(r => r.Product != null).Select(r => r.Product!));
            if (responses.Count < PerPage)
            {
                break;
            }
            page++;
        }
        return products;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await GetOptionalAsync<MaxioCustomerResponse>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            HttpMethod.Post, "customers.json", request, cancellationToken);
        return response.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var page = 1;
        var subscriptions = new List<MaxioSubscription>();
        while (true)
        {
            var responses = await GetAsync<List<MaxioSubscriptionResponse>>(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={PerPage}",
                HttpRequestMethod.Get, cancellationToken);
            subscriptions.AddRange(responses.Where(r => r.Subscription != null).Select(r => r.Subscription!));
            if (responses.Count < PerPage)
            {
                break;
            }
            page++;
        }
        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return response.Subscription;
    }

    public async Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await GetOptionalAsync<MaxioSubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return response?.Subscription;
    }

    private async Task<T> GetAsync<T>(string relativeUri, HttpRequestMethod method, CancellationToken ct)
    {
        using var httpResponse = await _httpClient.GetAsync(relativeUri, ct);
        var body = await httpResponse.Content.ReadAsStringAsync(ct);
        EnsureSuccess(httpResponse, body, method, relativeUri);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new MaxioApiException((int)httpResponse.StatusCode, body, new[] { "Empty response body." }, method, relativeUri);
    }

    private async Task<T?> GetOptionalAsync<T>(string relativeUri, CancellationToken ct) where T : class
    {
        using var httpResponse = await _httpClient.GetAsync(relativeUri, ct);
        var body = await httpResponse.Content.ReadAsStringAsync(ct);
        if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        EnsureSuccess(httpResponse, body, HttpRequestMethod.Get, relativeUri);
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod httpMethod, string relativeUri,
        TRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(httpMethod, relativeUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        using var httpResponse = await _httpClient.SendAsync(httpRequest, ct);
        var body = await httpResponse.Content.ReadAsStringAsync(ct);
        EnsureSuccess(httpResponse, body, HttpRequestMethod.Post, relativeUri);
        return JsonSerializer.Deserialize<TResponse>(body, JsonOptions)
            ?? throw new MaxioApiException((int)httpResponse.StatusCode, body, new[] { "Empty response body." }, HttpRequestMethod.Post, relativeUri);
    }

    private void EnsureSuccess(HttpResponseMessage httpResponse, string body, HttpRequestMethod method, string requestUri)
    {
        if (httpResponse.IsSuccessStatusCode)
        {
            _logger.LogInformation("Maxio API {Method} {Uri} -> {StatusCode}", method, requestUri, (int)httpResponse.StatusCode);
            return;
        }

        var errors = ParseErrors(body);
        _logger.LogWarning("Maxio API {Method} {Uri} -> {StatusCode}: {Errors}",
            method, requestUri, (int)httpResponse.StatusCode, string.Join("; ", errors));
        throw new MaxioApiException((int)httpResponse.StatusCode, body, errors, method, requestUri);
    }

    /// <summary>
    /// Parses Maxio error payloads (errors/Error-List-Response.yaml). The "errors"
    /// member is either an array of strings or a map of field -> messages.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new[] { "(empty error body)" };
        }

        try
        {
            var payload = JsonSerializer.Deserialize<MaxioErrorPayload>(body, JsonOptions);
            if (payload?.Errors is { Count: > 0 })
            {
                return payload.Errors;
            }

            // Lenient fallback: some Maxio error payloads carry field-scoped maps
            // under "errors" (e.g. { "errors": { "customer.email": ["..."] } }).
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Object)
            {
                return errorsElement.EnumerateObject()
                    .SelectMany(field => field.Value.ValueKind == JsonValueKind.Array
                        ? field.Value.EnumerateArray().Select(m => $"{field.Name}: {m.GetString()}")
                        : new[] { $"{field.Name}: {field.Value}" })
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return new[] { body };
    }
}