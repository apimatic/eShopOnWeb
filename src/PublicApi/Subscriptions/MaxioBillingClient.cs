using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// HTTP adapter for the operations defined in maxio-spec/openapi.yaml. It deliberately
/// exposes only the Maxio operations needed by this capability.
/// </summary>
internal sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly string _productFamilyHandle;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _productFamilyHandle = settings.ProductFamilyHandle;
        _httpClient.BaseAddress = BuildBaseAddress(settings);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // The OpenAPI contract defines BasicAuth; the API key is the username and `x` is the password.
        var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        // readProductFamily accepts handle:<handle> in its {id} path parameter, per the contract description.
        var family = await GetAsync<MaxioProductFamilyResponse>(
            $"product_families/handle:{Uri.EscapeDataString(_productFamilyHandle)}.json",
            "read product family", cancellationToken);

        var products = await GetAsync<List<MaxioProductResponse>>(
            $"product_families/{family.ProductFamily.Id}/products.json?per_page=200",
            "list products for product family", cancellationToken);

        var result = new List<MaxioProduct>(products.Count);
        foreach (var item in products)
        {
            if (item.Product.ArchivedAt is null && !string.IsNullOrWhiteSpace(item.Product.Handle))
            {
                result.Add(item.Product);
            }
        }
        return result;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        return await GetOrNotFoundAsync<MaxioCustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", "find customer", cancellationToken) is { } result
            ? result.Customer
            : null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken)
    {
        var result = await PostAsync<CreateCustomerRequest, MaxioCustomerResponse>("customers.json",
            new CreateCustomerRequest { Customer = customer }, "create customer", cancellationToken);
        return result.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", "list customer subscriptions", cancellationToken);
        var result = new List<MaxioSubscription>(subscriptions.Count);
        foreach (var item in subscriptions) result.Add(item.Subscription);
        return result;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        return await GetOrNotFoundAsync<MaxioSubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", "find subscription", cancellationToken) is { } result
            ? result.Subscription
            : null;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken)
    {
        var result = await PostAsync<CreateSubscriptionRequest, MaxioSubscriptionResponse>("subscriptions.json",
            new CreateSubscriptionRequest { Subscription = subscription }, "create subscription", cancellationToken);
        return result.Subscription;
    }

    private async Task<T> GetAsync<T>(string path, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await DeserializeResponse<T>(response, operation, cancellationToken);
    }

    private async Task<T?> GetOrNotFoundAsync<T>(string path, string operation, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await DeserializeResponse<T>(response, operation, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, string operation, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        return await DeserializeResponse<TResponse>(response, operation, cancellationToken);
    }

    private static async Task<T> DeserializeResponse<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new MaxioApiException(response.StatusCode, operation, body);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new MaxioApiException(response.StatusCode, operation, body);
    }

    private static Uri BuildBaseAddress(MaxioOptions settings)
    {
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? $"https://{settings.Subdomain}.chargify.com/"
            : settings.BaseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new OptionsValidationException(MaxioOptions.SectionName, typeof(MaxioOptions), new[] { "BaseUrl must be an absolute HTTPS URL." });
        }

        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/", UriKind.Absolute);
    }
}
