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

internal sealed class MaxioClient : IMaxioClient
{
    private const int PageSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _baseUri = options.Value.GetBaseUri();
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        using var response = await GetAsync("site.json", cancellationToken);
        return (await ReadRequiredAsync<MaxioSiteResponse>(response, "readSite", cancellationToken)).Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string familyHandle,
        CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var family = Uri.EscapeDataString($"handle:{familyHandle}");

        for (var page = 1; ; page++)
        {
            var path = $"product_families/{family}/products.json?page={page}&per_page={PageSize}&include_archived=false";
            using var response = await GetAsync(path, cancellationToken);
            var pageItems = await ReadRequiredAsync<List<MaxioProductResponse>>(
                response,
                "listProductsForProductFamily",
                cancellationToken);
            products.AddRange(pageItems.Select(item => item.Product));

            if (pageItems.Count < PageSize)
            {
                return products;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await GetAsync(path, cancellationToken, allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadRequiredAsync<MaxioCustomerResponse>(response, "readCustomerByReference", cancellationToken)).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        using var response = await PostAsync("customers.json", request, cancellationToken);
        return (await ReadRequiredAsync<MaxioCustomerResponse>(response, "createCustomer", cancellationToken)).Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await GetAsync(path, cancellationToken, allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadRequiredAsync<MaxioSubscriptionResponse>(response, "findSubscription", cancellationToken)).Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        using var response = await PostAsync("subscriptions.json", request, cancellationToken);
        return (await ReadRequiredAsync<MaxioSubscriptionResponse>(response, "createSubscription", cancellationToken)).Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        using var response = await GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var items = await ReadRequiredAsync<List<MaxioSubscriptionResponse>>(
            response,
            "listCustomerSubscriptions",
            cancellationToken);
        return items.Select(item => item.Subscription).ToList();
    }

    private async Task<HttpResponseMessage> GetAsync(
        string relativePath,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await _httpClient.GetAsync(BuildUri(relativePath), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            {
                return response;
            }

            if (attempt >= 3 || !IsTransient(response.StatusCode))
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
        }
    }

    private Task<HttpResponseMessage> PostAsync<T>(
        string relativePath,
        T body,
        CancellationToken cancellationToken)
    {
        var content = JsonContent.Create(body, options: JsonOptions);
        return _httpClient.PostAsync(BuildUri(relativePath), content, cancellationToken);
    }

    private Uri BuildUri(string relativePath) => new(_baseUri, relativePath);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, operation, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, operation, "The response body was empty.");
    }

    private static async Task<MaxioApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new MaxioApiException(response.StatusCode, operation, ExtractErrors(body));
    }

    private static string? ExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            return errors.ValueKind switch
            {
                JsonValueKind.Array => string.Join(" ", errors.EnumerateArray().Select(ValueText)),
                JsonValueKind.Object => string.Join(" ", errors.EnumerateObject().Select(p => $"{p.Name}: {ValueText(p.Value)}")),
                _ => ValueText(errors)
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ValueText(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : value.ToString();
}
