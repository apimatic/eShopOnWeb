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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. Every endpoint, path,
/// query parameter, request/response shape and the Basic auth scheme used here
/// come from the Maxio OpenAPI specification in maxio-spec/openapi.yaml.
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        var maxioOptions = options.Value;

        _httpClient.BaseAddress = new Uri(ResolveBaseUrl(maxioOptions));

        var apiKey = maxioOptions.ApiKey
            ?? throw new MaxioConfigurationException("Maxio:ApiKey is not configured. Set the MAXIO_API_KEY environment variable.");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string ResolveBaseUrl(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        var subdomain = options.Subdomain
            ?? throw new MaxioConfigurationException("Maxio:Subdomain is not configured. Set the MAXIO_SITE_SUBDOMAIN environment variable.");
        return $"https://{subdomain}.chargify.com";
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get, "product_families.json", null, cancellationToken);
        return Deserialize<List<MaxioProductFamilyEnvelope>>(json)
            .Select(e => e.ProductFamily)
            .Where(f => f is not null)
            .Cast<MaxioProductFamily>()
            .ToList();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get, "products.json", null, cancellationToken);
        return Deserialize<List<MaxioProductEnvelope>>(json)
            .Select(e => e.Product)
            .Where(p => p is not null)
            .Cast<MaxioProduct>()
            .ToList();
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var json = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
            return Deserialize<MaxioCustomerEnvelope>(json).Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post, "customers.json", new MaxioCreateCustomerEnvelope { Customer = request }, cancellationToken);
        return Deserialize<MaxioCustomerEnvelope>(json).Customer
            ?? throw new MaxioApiException(0, json, "Maxio returned an empty customer payload.");
    }

    public async Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var json = await SendAsync(HttpMethod.Get, $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
            return Deserialize<MaxioSubscriptionEnvelope>(json).Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post, "subscriptions.json", new MaxioCreateSubscriptionEnvelope { Subscription = request }, cancellationToken);
        return Deserialize<MaxioSubscriptionEnvelope>(json).Subscription
            ?? throw new MaxioApiException(0, json, "Maxio returned an empty subscription payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        return Deserialize<List<MaxioSubscriptionEnvelope>>(json)
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    private async Task<string> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(
                (int)response.StatusCode,
                content,
                $"Maxio API returned {(int)response.StatusCode} for {method} {path}. Response: {content}");
        }

        return content;
    }

    private static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new MaxioApiException(0, json, $"Maxio returned an unparseable payload for {typeof(T).Name}.");
    }
}
