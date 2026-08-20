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

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle);
        var items = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/handle:{family}/products.json?per_page=200", false, cancellationToken);
        return items!.Select(item => item.Product)
            .Where(product => product.ArchivedAt == null)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await GetAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", true, cancellationToken);
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName,
        string email, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            },
            uniqueness_token = uniquenessToken
        };
        var result = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body,
            cancellationToken);
        return result.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken)
    {
        var items = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", false, cancellationToken);
        return items!.Select(item => item.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle,
        string subscriptionReference, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                payment_collection_method = "remittance"
            },
            uniqueness_token = uniquenessToken
        };
        var result = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body,
            cancellationToken);
        return result.Subscription;
    }

    private async Task<T?> GetAsync<T>(string path, bool returnNullOnNotFound,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (returnNullOnNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken)
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }

    private static async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return result ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
        }

        var detail = await ReadErrorAsync(response, cancellationToken);
        throw new MaxioApiException(response.StatusCode, detail);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(JsonOptions, cancellationToken);
            if (error?.Errors is { Count: > 0 })
            {
                return string.Join(" ", error.Errors);
            }
        }
        catch (JsonException)
        {
            // Maxio can return a non-JSON proxy response; do not expose it to API callers.
        }

        return $"Maxio request failed with HTTP {(int)response.StatusCode}.";
    }

    private Uri BuildUri(string path)
    {
        return new Uri($"{_options.ResolveBaseUrl().TrimEnd('/')}/{path}", UriKind.Absolute);
    }
}
