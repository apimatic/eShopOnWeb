using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, string uniquenessToken, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, string uniquenessToken, CancellationToken cancellationToken);
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = _options.GetBaseAddress();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        using var document = (await GetAsync($"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200&include_archived=false", cancellationToken))!;
        return ReadItems(document.RootElement)
            .Select(item => JsonSerializer.Deserialize<MaxioProductListItem>(item.GetRawText(), JsonOptions)?.Product)
            .Where(product => product is not null)
            .Cast<MaxioProduct>()
            .ToArray();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"products/handle/{Uri.EscapeDataString(productHandle)}.json", cancellationToken, treatNotFoundAsNull: true);
        return document is null ? null : JsonSerializer.Deserialize<MaxioProductEnvelope>(document.RootElement.GetRawText(), JsonOptions)?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken, treatNotFoundAsNull: true);
        return document is null ? null : JsonSerializer.Deserialize<MaxioCustomerEnvelope>(document.RootElement.GetRawText(), JsonOptions)?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new { customer, uniqueness_token = uniquenessToken };
        using var document = await SendAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        return JsonSerializer.Deserialize<MaxioCustomerEnvelope>(document.RootElement.GetRawText(), JsonOptions)?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned no customer.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken, treatNotFoundAsNull: true);
        return document is null ? null : JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(document.RootElement.GetRawText(), JsonOptions)?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new { subscription, uniqueness_token = uniquenessToken };
        using var document = await SendAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        return JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(document.RootElement.GetRawText(), JsonOptions)?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned no subscription.");
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"subscriptions/{subscriptionId}.json", cancellationToken, treatNotFoundAsNull: true);
        return document is null ? null : JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(document.RootElement.GetRawText(), JsonOptions)?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var document = (await GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken))!;
        return ReadItems(document.RootElement)
            .Select(item => JsonSerializer.Deserialize<MaxioSubscriptionListItem>(item.GetRawText(), JsonOptions)?.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<MaxioSubscription>()
            .ToArray();
    }

    private async Task<JsonDocument?> GetAsync(string path, CancellationToken cancellationToken, bool treatNotFoundAsNull = false)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await ReadResponseAsync(response, treatNotFoundAsNull, cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return (await ReadResponseAsync(response, treatNotFoundAsNull: false, cancellationToken))!;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new MaxioConfigurationException("Maxio:ApiKey is required.");

        var request = new HttpRequestMessage(method, path);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<JsonDocument?> ReadResponseAsync(HttpResponseMessage response, bool treatNotFoundAsNull, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new MaxioApiException(response.StatusCode, body);

        return JsonDocument.Parse(body);
    }

    private static IEnumerable<JsonElement> ReadItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        if (root.ValueKind != JsonValueKind.Object)
            return Enumerable.Empty<JsonElement>();

        if (!root.TryGetProperty("items", out var items))
            return Enumerable.Empty<JsonElement>();

        if (items.ValueKind == JsonValueKind.Array)
            return items.EnumerateArray();

        return items.ValueKind == JsonValueKind.Object
            ? new[] { items }
            : Enumerable.Empty<JsonElement>();
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio API returned {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}
