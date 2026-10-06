using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing (Billing API).
/// Authentication is HTTP Basic over TLS: the API key is the username and "X" is the password.
/// </summary>
public class MaxioApiClient
{
    public const string HttpClientName = "MaxioApi";

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the Billing API base address: the explicit Maxio:BaseUrl override when present,
    /// otherwise a host derived from the subdomain and hosting environment.
    /// </summary>
    public static string ResolveBaseUrl(MaxioOptions options, string? environment = null)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        if (string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{options.Subdomain}.ebilling.maxio.com";
        }

        return $"https://{options.Subdomain}.chargify.com";
    }

    /// <summary>
    /// Configures the named HttpClient used by this client. Call from DI setup.
    /// </summary>
    public static void ConfigureHttpClient(HttpClient httpClient, MaxioOptions options, string? environment = null)
    {
        if (!options.IsValid)
        {
            // Configuration is supplied via user secrets / environment variables; a request that
            // actually reaches Maxio will fail with a clear error, but the app must still start.
            return;
        }

        httpClient.BaseAddress = new Uri(ResolveBaseUrl(options, environment) + "/");
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:X"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle)
    {
        var products = await GetJsonAsync<List<MaxioWrappedProduct>>(
            $"product_families/{Uri.EscapeDataString("handle:" + productFamilyHandle)}/products.json",
            NotFoundBehavior.Throw);

        return products?
            .Where(p => p.Product?.Handle != null)
            .Select(p => p.Product)
            .ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioSite> ReadSiteAsync()
    {
        var wrapped = await GetJsonAsync<MaxioWrappedSite>("site.json", NotFoundBehavior.Throw);
        return wrapped?.Site ?? throw new MaxioApiException("Maxio site read returned an empty response.", 500);
    }

    /// <summary>Returns the customer with the given reference, or null when none exists.</summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference)
    {
        var wrapped = await GetJsonAsync<MaxioWrappedCustomer>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            NotFoundBehavior.ReturnNull);
        return wrapped?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer)
    {
        var wrapped = await PostJsonAsync<MaxioWrappedCustomer>("customers.json", new MaxioCreateCustomerRequest { Customer = customer });
        return wrapped?.Customer ?? throw new MaxioApiException("Maxio customer creation returned an empty response.", 500);
    }

    /// <summary>Returns the subscription with the given reference, or null when none exists.</summary>
    public async Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference)
    {
        var wrapped = await GetJsonAsync<MaxioWrappedSubscription>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            NotFoundBehavior.ReturnNull);
        return wrapped?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription)
    {
        var wrapped = await PostJsonAsync<MaxioWrappedSubscription>("subscriptions.json", new MaxioCreateSubscriptionRequest { Subscription = subscription });
        return wrapped?.Subscription ?? throw new MaxioApiException("Maxio subscription creation returned an empty response.", 500);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var subscriptions = await GetJsonAsync<List<MaxioWrappedSubscription>>(
            $"customers/{customerId}/subscriptions.json",
            NotFoundBehavior.Throw);

        return subscriptions?.Select(s => s.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private enum NotFoundBehavior { ReturnNull, Throw }

    private async Task<T?> GetJsonAsync<T>(string requestUri, NotFoundBehavior notFoundBehavior)
    {
        _logger.LogInformation("Maxio GET {RequestUri}", requestUri);
        using var response = await _httpClient.GetAsync(requestUri);

        if (response.StatusCode == HttpStatusCode.NotFound && notFoundBehavior == NotFoundBehavior.ReturnNull)
        {
            return default;
        }

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw CreateException(response.StatusCode, body, requestUri);
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(body, MaxioJson.Options);
    }

    private async Task<T?> PostJsonAsync<T>(string requestUri, object payload)
    {
        _logger.LogInformation("Maxio POST {RequestUri}", requestUri);
        using var response = await _httpClient.PostAsJsonAsync(requestUri, payload, MaxioJson.Options);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw CreateException(response.StatusCode, body, requestUri);
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(body, MaxioJson.Options);
    }

    private static MaxioApiException CreateException(HttpStatusCode statusCode, string body, string requestUri)
    {
        var errors = MaxioJson.TryExtractErrors(body);
        return new MaxioApiException(
            $"Maxio Billing API request failed ({(int)statusCode} {statusCode}): {requestUri}. {errors}",
            (int)statusCode,
            body);
    }
}