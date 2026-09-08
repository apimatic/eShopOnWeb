using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API. Built directly against the
/// Maxio OpenAPI specification (maxio-spec/openapi.yaml):
/// - Auth: Basic, username = API key, password = "x" (spec securitySchemes.BasicAuth).
/// - Base URL: "Maxio:BaseUrl" when set (used verbatim), otherwise derived from
///   the subdomain and environment per the spec's x-server-configuration
///   (US: https://{site}.chargify.com, EU: https://{site}.ebilling.maxio.com).
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const int ProductPageSize = 200; // spec: maximum allowed per_page is 200
    private const int MaxProductPages = 100; // safety bound against unexpected pagination behavior

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio integration is not configured: 'Maxio:ApiKey' is missing.");
        }

        _httpClient = httpClient;

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(settings.BaseUrl);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new InvalidOperationException(
                    "Maxio integration is not configured: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
            }

            _httpClient.BaseAddress = IsEuEnvironment(settings.Environment)
                ? new Uri($"https://{settings.Subdomain}.ebilling.maxio.com/")
                : new Uri($"https://{settings.Subdomain}.chargify.com/");
        }

        // The `username` is a Maxio Chargify API key. The `password` is `x`.
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static bool IsEuEnvironment(string environment) =>
        string.Equals(environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        // GET /customers/lookup.json?reference={reference} -> Customer-Response | 404
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return payload?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerBody customer, CancellationToken cancellationToken = default)
    {
        // POST /customers.json { customer: {...} } -> Customer-Response
        var response = await _httpClient.PostAsync("customers.json",
            new StringContent(JsonSerializer.Serialize(new CreateMaxioCustomerPayload { Customer = customer }, JsonOptions), Encoding.UTF8, "application/json"),
            cancellationToken);
        var payload = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return payload!.Customer!;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        // GET /products.json?page={page}&per_page=200 -> array of Product-Response
        var products = new List<MaxioProduct>();
        for (var page = 1; page <= MaxProductPages; page++)
        {
            var response = await _httpClient.GetAsync($"products.json?page={page}&per_page={ProductPageSize}", cancellationToken);
            var payload = await ReadAsync<List<MaxioProductResponse>>(response, cancellationToken) ?? new List<MaxioProductResponse>();

            foreach (var item in payload)
            {
                if (item.Product != null)
                {
                    products.Add(item.Product);
                }
            }

            if (payload.Count < ProductPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        // GET /subscriptions/lookup.json?reference={reference} -> Subscription-Response | 404
        var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return payload?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        // GET /customers/{customer_id}/subscriptions.json -> array of Subscription-Response
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var payload = await ReadAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken) ?? new List<MaxioSubscriptionResponse>();

        var subscriptions = new List<MaxioSubscription>();
        foreach (var item in payload)
        {
            if (item.Subscription != null)
            {
                subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionBody subscription, CancellationToken cancellationToken = default)
    {
        // POST /subscriptions.json { subscription: {...} } -> Subscription-Response (201)
        var response = await _httpClient.PostAsync("subscriptions.json",
            new StringContent(JsonSerializer.Serialize(new CreateMaxioSubscriptionPayload { Subscription = subscription }, JsonOptions), Encoding.UTF8, "application/json"),
            cancellationToken);
        var payload = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return payload!.Subscription!;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(
                (int)response.StatusCode,
                body,
                $"Maxio API call '{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}' returned {(int)response.StatusCode}: {body}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}
