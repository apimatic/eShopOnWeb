using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _apiBaseUri;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _apiBaseUri = options.Value.GetApiBaseUri();

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.Value.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetRequiredAsync<SiteResponse>("site.json", cancellationToken);
        return response.Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var family = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var response = await GetRequiredAsync<List<ProductResponse>>(
            $"product_families/{family}/products.json?per_page=200",
            cancellationToken);
        return response.Select(item => item.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var response = await GetOptionalAsync<CustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest(
            new CreateCustomer(firstName, lastName, email, reference),
            uniquenessToken);
        var response = await PostAsync<CreateCustomerRequest, CustomerResponse>("customers.json", request, cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var response = await GetOptionalAsync<SubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle,
        long customerId,
        string reference,
        string uniquenessToken,
        string paymentCollectionMethod,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest(
            new CreateSubscription(productHandle, customerId, reference, paymentCollectionMethod),
            uniquenessToken);
        var response = await PostAsync<CreateSubscriptionRequest, SubscriptionResponse>(
            "subscriptions.json",
            request,
            cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetRequiredAsync<List<SubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);
        return response.Select(item => item.Subscription).ToList();
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(BuildUri(path), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await _httpClient.GetAsync(BuildUri(path), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(BuildUri(path), request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private Uri BuildUri(string path)
    {
        return new Uri($"{_apiBaseUri.AbsoluteUri.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(
            (int)response.StatusCode,
            "Maxio returned an empty or invalid response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadErrorDetailAsync(response, cancellationToken);
        throw new MaxioApiException(
            (int)response.StatusCode,
            $"Maxio request failed with status {(int)response.StatusCode}{detail}.");
    }

    private static async Task<string> ReadErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return string.Empty;
            }

            var text = errors.ValueKind switch
            {
                JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(item => item.ToString())),
                JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(item => $"{item.Name}: {item.Value}")),
                _ => errors.ToString()
            };

            return string.IsNullOrWhiteSpace(text) ? string.Empty : $": {text}";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private sealed record SiteResponse(MaxioSite Site);
    private sealed record ProductResponse(MaxioProduct Product);
    private sealed record CustomerResponse(MaxioCustomer Customer);
    private sealed record SubscriptionResponse(MaxioSubscription Subscription);
    private sealed record CreateCustomerRequest(CreateCustomer Customer, string UniquenessToken);
    private sealed record CreateCustomer(string FirstName, string LastName, string Email, string Reference);
    private sealed record CreateSubscriptionRequest(CreateSubscription Subscription, string UniquenessToken);
    private sealed record CreateSubscription(
        string ProductHandle,
        long CustomerId,
        string Reference,
        string PaymentCollectionMethod);
}
