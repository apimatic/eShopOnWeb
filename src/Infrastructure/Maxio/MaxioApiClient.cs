using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/> built against the Maxio
/// Advanced Billing OpenAPI specification (maxio-spec/openapi.yaml).
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static void Configure(HttpClient httpClient, MaxioOptions options, string? environment)
    {
        var baseUrl = ResolveBaseUrl(options, environment);
        httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Resolves the API base address per the spec's server configuration:
    /// Maxio:BaseUrl wins verbatim when set; otherwise the base URL is derived
    /// from the site subdomain using the spec's server templates
    /// (US: https://{site}.chargify.com, EU: https://{site}.ebilling.maxio.com).
    /// </summary>
    public static string ResolveBaseUrl(MaxioOptions options, string? environment)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: either '{MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)}' or '{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)}' must be set.");
        }

        var isEu = string.Equals(environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
        return isEu
            ? $"https://{options.Subdomain.Trim()}.ebilling.maxio.com"
            : $"https://{options.Subdomain.Trim()}.chargify.com";
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var products = await GetListAsync<MaxioProductResponse>($"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json", cancellationToken);
        return products.Select(p => p.Product).Where(p => p.ArchivedAt is null).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        return await FindAsync<MaxioCustomerResponse, MaxioCustomer>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", r => r.Customer, cancellationToken);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<CreateMaxioCustomerRequest, MaxioCustomerResponse>("customers.json", new CreateMaxioCustomerRequest { Customer = customer }, cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        return await FindAsync<MaxioSubscriptionResponse, MaxioSubscription>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", r => r.Subscription, cancellationToken);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<CreateMaxioSubscriptionRequest, MaxioSubscriptionResponse>("subscriptions.json", new CreateMaxioSubscriptionRequest { Subscription = subscription }, cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetListAsync<MaxioSubscriptionResponse>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return subscriptions.Select(s => s.Subscription).ToList();
    }

    private async Task<TResponse[]> GetListAsync<TResponse>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await MaxioApiExceptionAsync(response, cancellationToken);
        }
        return await response.Content.ReadFromJsonAsync<TResponse[]>(SerializerOptions, cancellationToken)
            ?? Array.Empty<TResponse>();
    }

    private async Task<TModel?> FindAsync<TResponse, TModel>(string relativeUrl, Func<TResponse, TModel> selector, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw await MaxioApiExceptionAsync(response, cancellationToken);
        }
        var payload = await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
        return payload is null ? default : selector(payload);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativeUrl, body, SerializerOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await MaxioApiExceptionAsync(response, cancellationToken);
        }
        var payload = await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
        return payload ?? throw new MaxioApiException(response.StatusCode, "Empty response from Maxio API.");
    }

    private static async Task<MaxioApiException> MaxioApiExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            // Best effort: keep the exception even if the body can't be read.
        }
        return new MaxioApiException(response.StatusCode, body);
    }
}