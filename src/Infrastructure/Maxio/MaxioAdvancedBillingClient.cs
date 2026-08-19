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
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        ConfigureHttpClient(_httpClient, _options);
    }

    internal static void ConfigureHttpClient(HttpClient httpClient, MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new MaxioConfigurationException($"{MaxioOptions.ApiKeyKey} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new MaxioConfigurationException($"{MaxioOptions.SubdomainKey} or {MaxioOptions.BaseUrlKey} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException($"{MaxioOptions.ProductFamilyHandleKey} is required.");
        }

        var baseUrl = options.ResolveBaseUrl();
        httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X")));
    }

    public string ProductFamilyHandle => _options.ProductFamilyHandle;

    internal async Task<IReadOnlyList<ProductDto>> ListProductsForFamilyAsync(CancellationToken cancellationToken)
    {
        var familyHandle = Uri.EscapeDataString(_options.ProductFamilyHandle);
        // Colon is encoded so HttpClient does not treat the relative path as a URI scheme.
        var path = $"product_families/handle%3A{familyHandle}/products.json?per_page=200";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioConfigurationException(
                $"Maxio product family '{_options.ProductFamilyHandle}' was not found.");
        }

        await EnsureSuccessAsync(response, "list products for product family", cancellationToken);
        var envelopes = await DeserializeAsync<List<ProductEnvelope>>(response, cancellationToken)
                        ?? new List<ProductEnvelope>();
        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Cast<ProductDto>()
            .ToList();
    }

    internal async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "read customer by reference", cancellationToken);
        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    internal async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(HttpMethod.Post, "customers.json", request, cancellationToken);
        await EnsureSuccessAsync(response, "create customer", cancellationToken);
        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioBillingException("Maxio create customer returned an empty body.", (int)response.StatusCode);
        }

        return envelope.Customer;
    }

    internal async Task<SubscriptionDto?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "find subscription", cancellationToken);
        var envelope = await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription;
    }

    internal async Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionDto>();
        }

        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);
        var envelopes = await DeserializeAsync<List<SubscriptionEnvelope>>(response, cancellationToken)
                        ?? new List<SubscriptionEnvelope>();
        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Cast<SubscriptionDto>()
            .ToList();
    }

    internal async Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        await EnsureSuccessAsync(response, "create subscription", cancellationToken);
        var envelope = await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioBillingException("Maxio create subscription returned an empty body.", (int)response.StatusCode);
        }

        return envelope.Subscription;
    }

    internal async Task<bool> IsDuplicateReferenceConflictAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not HttpStatusCode.UnprocessableEntity and not HttpStatusCode.Conflict)
        {
            return false;
        }

        await response.Content.LoadIntoBufferAsync();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains("reference", StringComparison.OrdinalIgnoreCase)
               && (body.Contains("unique", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("already", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("taken", StringComparison.OrdinalIgnoreCase));
    }

    internal async Task<HttpResponseMessage> SendCreateCustomerRawAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
        => await SendJsonAsync(HttpMethod.Post, "customers.json", request, cancellationToken);

    internal async Task<HttpResponseMessage> SendCreateSubscriptionRawAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
        => await SendJsonAsync(HttpMethod.Post, "subscriptions.json", request, cancellationToken);

    private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string path, T body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(method, path) { Content = content };
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Maxio {Operation} failed with {StatusCode}: {Body}", operation, (int)response.StatusCode, Truncate(body));
        throw new MaxioBillingException(
            $"Maxio {operation} failed with status {(int)response.StatusCode}.",
            (int)response.StatusCode,
            body);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, MaxioJson.Options, cancellationToken);
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 500 ? value : value[..500];
    }
}
