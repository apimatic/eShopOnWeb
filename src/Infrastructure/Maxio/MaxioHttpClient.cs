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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP transport for the Maxio Billing API. Authenticates with HTTP Basic
/// (API key as username, "x" as password) over TLS, per the Billing API
/// authentication contract.
/// </summary>
public class MaxioHttpClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioHttpClient> _logger;

    public MaxioHttpClient(HttpClient httpClient, IOptions<MaxioSettings> settings,
        ILogger<MaxioHttpClient> logger)
    {
        _settings = settings.Value;
        _settings.Validate();
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.GetApiBaseUrl() + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken = default)
    {
        var site = await GetJsonAsync<SiteEnvelope>("site.json", cancellationToken);
        return site?.Site?.Currency ?? "USD";
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var products = await GetJsonAsync<List<ProductEnvelope>>(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json",
            cancellationToken, treatNotFoundAsEmpty: true);
        return products?
            .Where(p => p.Product is not null && p.Product.ArchivedAt is null)
            .Select(p => p.Product!)
            .ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        // The lookup endpoint returns a single match (or 404).
        var envelope = await GetJsonAsync<CustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken, treatNotFoundAsEmpty: true);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName,
        string email, string reference, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };
        var envelope = await PostJsonAsync<CustomerEnvelope>("customers.json", body, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(500, "Maxio customer creation returned an empty response.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId,
        string reference, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                payment_collection_method = "invoice"
            }
        };
        var envelope = await PostJsonAsync<SubscriptionEnvelope>("subscriptions.json", body, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(500, "Maxio subscription creation returned an empty response.");
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var envelope = await GetJsonAsync<SubscriptionEnvelope>($"subscriptions/{subscriptionId}.json",
            cancellationToken, treatNotFoundAsEmpty: true);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription?> GetSubscriptionByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        var envelope = await GetJsonAsync<SubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken, treatNotFoundAsEmpty: true);
        return envelope?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetJsonAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken, treatNotFoundAsEmpty: true);
        return subscriptions?
            .Where(s => s.Subscription is not null)
            .Select(s => s.Subscription!)
            .ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CancelSubscriptionAsync(int subscriptionId, string message,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendWithRetryAsync<SubscriptionEnvelope>(
            () => new HttpRequestMessage(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json?message={Uri.EscapeDataString(message)}"),
            cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(500, "Maxio subscription cancellation returned an empty response.");
    }

    private async Task<T?> GetJsonAsync<T>(string relativeUrl, CancellationToken cancellationToken,
        bool treatNotFoundAsEmpty = false) where T : class
    {
        return await SendWithRetryAsync<T>(
            () => new HttpRequestMessage(HttpMethod.Get, relativeUrl),
            cancellationToken, treatNotFoundAsEmpty);
    }

    private async Task<T?> PostJsonAsync<T>(string relativeUrl, object body,
        CancellationToken cancellationToken) where T : class
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return await SendWithRetryAsync<T>(
            () => new HttpRequestMessage(HttpMethod.Post, relativeUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends a request with Basic auth, retrying transient failures (5xx and
    /// network errors) with linear backoff. Non-retryable failures surface as
    /// <see cref="MaxioApiException"/> unless the caller opted to treat 404 as empty.
    /// </summary>
    private async Task<T?> SendWithRetryAsync<T>(Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken, bool treatNotFoundAsEmpty = false) where T : class
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = requestFactory();
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        return null;
                    }
                    return JsonSerializer.Deserialize<T>(body, JsonOptions);
                }

                if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsEmpty)
                {
                    return null;
                }

                throw new MaxioApiException((int)response.StatusCode, Truncate(body));
            }
            catch (MaxioApiException)
            {
                throw;
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException)
                                        && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogError(ex, "Maxio API call failed after {Attempts} attempts", attempt);
                    throw new MaxioApiException(502, $"Maxio API unreachable after {attempt} attempts: {ex.Message}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value : value.Substring(0, 1000);
}