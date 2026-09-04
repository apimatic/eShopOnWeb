using System;
using System.Collections.Generic;
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
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?per_page=200",
            null, cancellationToken);
        return DeserializeCollection<MaxioProductEnvelope>(response).ConvertAll(item => item.Product);
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(502, "Maxio returned an empty customer response.");
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            },
            uniqueness_token = uniquenessToken
        };
        var response = await SendAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
        var envelope = JsonSerializer.Deserialize<MaxioCustomerEnvelope>(response, JsonOptions);
        return envelope?.Customer ?? throw new MaxioApiException(502, "Maxio returned an empty customer response.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                payment_collection_method = "invoice"
            },
            uniqueness_token = uniquenessToken
        };
        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var envelope = JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(response, JsonOptions);
        return envelope?.Subscription ?? throw new MaxioApiException(502, "Maxio returned an empty subscription response.");
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", null, cancellationToken);
        var envelope = JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(response, JsonOptions);
        return envelope?.Subscription ?? throw new MaxioApiException(502, "Maxio returned an empty subscription response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var all = new List<MaxioSubscription>();
        for (var page = 1; ; page++)
        {
            var response = await SendAsync(HttpMethod.Get, $"subscriptions.json?page={page}&per_page=200", null, cancellationToken);
            var items = DeserializeCollection<MaxioSubscriptionListEnvelope>(response);
            all.AddRange(items.ConvertAll(item => item.Subscription));
            if (items.Count < 200)
            {
                return all;
            }
        }
    }

    private async Task<string> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, body, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        _options.Validate();
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = _options.GetBaseAddress();
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync();
        throw new MaxioApiException((int)response.StatusCode, detail);
    }

    private static List<T> DeserializeCollection<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
        }

        if (document.RootElement.TryGetProperty("items", out var items))
        {
            return JsonSerializer.Deserialize<List<T>>(items.GetRawText(), JsonOptions) ?? new List<T>();
        }

        return new List<T>();
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string detail) : base($"Maxio returned HTTP {statusCode}.")
    {
        StatusCode = statusCode;
        Detail = detail;
    }

    public int StatusCode { get; }
    public string Detail { get; }
}
