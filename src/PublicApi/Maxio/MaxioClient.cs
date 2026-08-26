using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Plain-HTTP client for the Maxio Advanced Billing API.
/// Verified against the live sandbox and the official docs:
///  - Basic auth: API key as username, literal "x" as password.
///  - GET  /product_families/handle:{handle}/products.json  -> [{ "product": {...} }, ...]
///  - GET  /customers/lookup.json?reference={ref}           -> { "customer": {...} } or 404
///  - POST /customers.json                                  -> { "customer": {...} }
///  - POST /subscriptions.json                              -> { "subscription": {...} }
///  - GET  /subscriptions.json?customer_id={id}             -> [{ "subscription": {...} }, ...]
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        // The product_family_id path parameter accepts the family handle prefixed with "handle:".
        var familyKey = Uri.EscapeDataString($"handle:{_settings.ProductFamilyHandle}");
        var wrappers = await SendAsync<List<MaxioProductWrapper>>(
            HttpMethod.Get, $"product_families/{familyKey}/products.json", body: null, cancellationToken);

        return wrappers.Select(w => w.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendRawAsync(HttpMethod.Get, path, body: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadAsync<MaxioCustomerWrapper>(response, path, cancellationToken)).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var wrapper = await SendAsync<MaxioCustomerWrapper>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return wrapper.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };

        var wrapper = await SendAsync<MaxioSubscriptionWrapper>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return wrapper.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var wrappers = await SendAsync<List<MaxioSubscriptionWrapper>>(
            HttpMethod.Get, $"subscriptions.json?customer_id={customerId}", body: null, cancellationToken);

        return wrappers.Select(w => w.Subscription).ToList();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, body, cancellationToken);
        return await ReadAsync<T>(response, path, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        _settings.ThrowIfNotConfigured();

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        _logger.LogDebug("Maxio {Method} {Path} -> {StatusCode}", method, path, (int)response.StatusCode);
        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, content);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new MaxioApiException(response.StatusCode, $"Empty response body from '{path}'.");
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, $"Unparseable response from '{path}': {ex.Message}");
        }
    }
}
