using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioClient"/> using Basic authentication against
/// the Maxio (Advanced Billing) API.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        MaxioOptions opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured. Set the '{MaxioOptions.SectionName}:ApiKey' configuration value (e.g. via user-secrets from the MAXIO_API_KEY environment variable).");
        }

        string baseUrl = !string.IsNullOrWhiteSpace(opts.BaseUrl)
            ? opts.BaseUrl.TrimEnd('/')
            : $"https://{opts.Subdomain}.chargify.com";

        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.ApiKey}:X")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomerDto?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        string url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<CustomerResponse>(response, cancellationToken) is { } result
            ? result.Customer
            : null;
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        using var content = Serialize(request);
        using var response = await _httpClient.PostAsync("customers.json", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await DeserializeAsync<CustomerResponse>(response, cancellationToken);
        return result?.Customer ?? throw new MaxioApiException(response.StatusCode, null, "Maxio returned an empty customer response.");
    }

public async Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        string url = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await DeserializeAsync<List<ProductListResponse>>(response, cancellationToken);
        return items?.Select(i => i.Product).Where(p => p is not null).ToList() ?? new List<MaxioProductDto>();
    }

    public async Task<MaxioProductDto?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        string url = $"products/handle/{Uri.EscapeDataString(handle)}.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<ProductResponse>(response, cancellationToken) is { } result
            ? result.Product
            : null;
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        using var content = Serialize(request);
        using var response = await _httpClient.PostAsync("subscriptions.json", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await DeserializeAsync<SubscriptionResponse>(response, cancellationToken);
        return result?.Subscription ?? throw new MaxioApiException(response.StatusCode, null, "Maxio returned an empty subscription response.");
    }

    public async Task<MaxioSubscriptionDto> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        string url = $"subscriptions/{subscriptionId}.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await DeserializeAsync<SubscriptionResponse>(response, cancellationToken);
        return result?.Subscription ?? throw new MaxioApiException(response.StatusCode, null, "Maxio returned an empty subscription response.");
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        string url = $"customers/{customerId}/subscriptions.json?per_page=200";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await DeserializeAsync<List<SubscriptionListResponse>>(response, cancellationToken);
        return items?.Select(i => i.Subscription).Where(s => s is not null).ToList() ?? new List<MaxioSubscriptionDto>();
    }

    private static StringContent Serialize<T>(T value)
    {
        return new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Maxio API returned {StatusCode} for {Method} {Url}: {Body}",
            (int)response.StatusCode, response.RequestMessage?.Method, response.RequestMessage?.RequestUri, body);
        throw new MaxioApiException(
            response.StatusCode,
            body,
            $"Maxio API returned {(int)response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}.");
    }
}
