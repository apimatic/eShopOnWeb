using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API. Authenticates with the site API key via HTTP Basic auth.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var maxioOptions = options.Value;
        _httpClient.BaseAddress = maxioOptions.GetBaseUrl();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return payload?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return payload?.Customer ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty customer payload.");
    }

    public async Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("product_families.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<List<MaxioProductFamilyResponse>>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            return null;
        }

        foreach (var item in payload)
        {
            if (string.Equals(item.ProductFamily.Handle, handle, StringComparison.OrdinalIgnoreCase))
            {
                return item.ProductFamily;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(int productFamilyId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"product_families/{productFamilyId}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            return Array.Empty<MaxioProduct>();
        }

        var products = new List<MaxioProduct>(payload.Count);
        foreach (var item in payload)
        {
            products.Add(item.Product);
        }

        return products;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken);
        return payload?.Subscription ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(payload.Count);
        foreach (var item in payload)
        {
            subscriptions.Add(item.Subscription);
        }

        return subscriptions;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Maxio error response body.");
        }

        string message = $"Maxio Advanced Billing API returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        if (!string.IsNullOrWhiteSpace(body))
        {
            message += $" Response: {body}";
        }

        throw new MaxioApiException(response.StatusCode, message, body);
    }
}
