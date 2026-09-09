using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API.
/// Built by hand strictly against the bundled OpenAPI specification (maxio-spec/openapi.yaml):
/// - Basic auth with the API key as username and the literal password "x" (spec securityScheme BasicAuth).
/// - JSON endpoints under https://{site}.chargify.com (spec server template; Maxio:BaseUrl overrides verbatim).
/// </summary>
public class MaxioApiClient
{
    private const int ProductsPerPage = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var maxioOptions = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(maxioOptions.GetEffectiveBaseUrl() + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>GET /customers/lookup.json?reference={reference} — Read Customer by Reference.</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken))?.Customer;
    }

    /// <summary>POST /customers.json — Create Customer.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("customers.json", new CreateMaxioCustomerRequest { Customer = customer }, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken))?.Customer
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Customer response was empty." }, null);
    }

    /// <summary>
    /// GET /products.json — List Products, following the spec's page/per_page pagination.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var response = await _httpClient.GetAsync($"products.json?page={page}&per_page={ProductsPerPage}", cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var batch = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOptions, cancellationToken)
                ?? new List<MaxioProductResponse>();
            products.AddRange(batch.Select(b => b.Product!).Where(p => p is not null));
            if (batch.Count < ProductsPerPage)
            {
                return products;
            }

            page++;
        }
    }

    /// <summary>GET /subscriptions/lookup.json?reference={reference} — Find Subscription.</summary>
    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken))?.Subscription;
    }

    /// <summary>POST /subscriptions.json — Create Subscription.</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string? reference, CancellationToken cancellationToken)
    {
        var body = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference
            }
        };
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken))?.Subscription
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Subscription response was empty." }, null);
    }

    /// <summary>GET /customers/{customer_id}/subscriptions.json — List Customer Subscriptions.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var batch = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions, cancellationToken)
            ?? new List<MaxioSubscriptionResponse>();
        return batch.Select(b => b.Subscription!).Where(s => s is not null).ToList();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = new List<string>();
        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorListResponse>(rawBody, JsonOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                errors.AddRange(parsed.Errors);
            }
        }
        catch (JsonException)
        {
            // Not the spec's error shape; the raw body is included in the exception message instead.
        }

        throw new MaxioApiException((int)response.StatusCode, errors, rawBody);
    }
}
