using System;
using System.Collections.Generic;
using System.Linq;
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
/// HTTP client for the Maxio Advanced Billing (Billing API) REST service.
/// Authentication is HTTP Basic with the API key as username and "x" as password,
/// as required by the Billing API.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;

        var maxioOptions = options.Value;
        var baseUrl = !string.IsNullOrWhiteSpace(maxioOptions.BaseUrl)
            ? maxioOptions.BaseUrl
            : $"https://{maxioOptions.Subdomain}.chargify.com";

        _httpClient.BaseAddress = new Uri(EnsureTrailingSlashRemoved(baseUrl));
        _httpClient.Timeout = TimeSpan.FromSeconds(60);

        var authBytes = Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle%3A{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, allowNotFound: false, cancellationToken);
        return envelopes!.Select(e => e.Product).Where(p => p.ArchivedAt == null).ToList();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var envelope = await GetAsync<MaxioProductEnvelope>(path, allowNotFound: true, cancellationToken);
        return envelope?.Product;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetAsync<MaxioCustomerEnvelope>(path, allowNotFound: true, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken = default)
    {
        try
        {
            var envelope = await PostAsync<MaxioCustomerEnvelope, Dictionary<string, MaxioCustomerCreate>>(
                "customers.json", new Dictionary<string, MaxioCustomerCreate> { ["customer"] = customer }, cancellationToken);
            return envelope!.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            throw new MaxioReferenceTakenException(
                $"A Maxio customer with reference '{customer.Reference}' already exists. {ex.Message}",
                ex.StatusCode, ex.ResponseBody);
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object>
        {
            ["subscription"] = new Dictionary<string, object>
            {
                ["product_handle"] = productHandle,
                ["customer_id"] = customerId,
                // Signups without card capture are only possible on invoice-style
                // collection. Which enum value is valid depends on the site
                // architecture: Relationship Invoicing sites use "remittance",
                // legacy Statements Architecture sites use "invoice".
                ["payment_collection_method"] = await GetPaymentCollectionMethodAsync(cancellationToken),
            }
        };

        var envelope = await PostAsync<MaxioSubscriptionEnvelope, Dictionary<string, object>>(
            "subscriptions.json", body, cancellationToken);
        return envelope!.Subscription;
    }

    private string? _paymentCollectionMethod;

    private async Task<string> GetPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_paymentCollectionMethod != null)
        {
            return _paymentCollectionMethod;
        }

        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", allowNotFound: false, cancellationToken);
        _paymentCollectionMethod = envelope?.Site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";
        return _paymentCollectionMethod;
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/{subscriptionId}.json";
        var envelope = await GetAsync<MaxioSubscriptionEnvelope>(path, allowNotFound: true, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json?per_page=200";
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(path, allowNotFound: true, cancellationToken);
        return envelopes?.Select(e => e.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<T?> GetAsync<T>(string path, bool allowNotFound, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<T>(body, _jsonOptions);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && allowNotFound)
        {
            return null;
        }

        throw new MaxioApiException($"Maxio Billing API request to '{path}' failed with status {(int)response.StatusCode}.", (int)response.StatusCode, body);
    }

    private async Task<TResponse?> PostAsync<TResponse, TRequest>(string path, TRequest request, CancellationToken cancellationToken) where TResponse : class
    {
        using var response = await _httpClient.PostAsJsonAsync(path, request, _jsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<TResponse>(body, _jsonOptions);
        }

        throw new MaxioApiException($"Maxio Billing API request to '{path}' failed with status {(int)response.StatusCode}.", (int)response.StatusCode, body);
    }

    private static string EnsureTrailingSlashRemoved(string baseUrl) => baseUrl.TrimEnd('/');
}