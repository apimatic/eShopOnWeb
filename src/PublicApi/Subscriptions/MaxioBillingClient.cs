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
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/{Uri.EscapeDataString($"handle:{productFamilyHandle}")}/products.json?page=1&per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return envelopes.Select(envelope => envelope.Product).Where(product => product.Id != 0).ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        return await SendForLookupAsync<MaxioCustomerResponse, MaxioCustomer>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            response => response.Customer,
            cancellationToken);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new CreateMaxioCustomerRequest { Customer = customer },
            cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        return await SendForLookupAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            response => response.Subscription,
            cancellationToken);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new CreateMaxioSubscriptionRequest
            {
                Subscription = new CreateMaxioSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = "remittance",
                    Reference = reference
                }
            },
            cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return envelopes.Select(envelope => envelope.Subscription).Where(subscription => subscription.Id != 0).ToArray();
    }

    public async Task<MaxioSubscription> ReadSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json",
            null,
            cancellationToken);
        return response.Subscription;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var request = CreateRequest(method, path, body);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await ReadResponseAsync<T>(response, path, cancellationToken);
    }

    private async Task<T?> SendForLookupAsync<TEnvelope, T>(string path, Func<TEnvelope, T> selector, CancellationToken cancellationToken)
        where TEnvelope : class
    {
        var request = CreateRequest(HttpMethod.Get, path, null);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        var envelope = await ReadResponseAsync<TEnvelope>(response, path, cancellationToken);
        return selector(envelope);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        }

        var request = new HttpRequestMessage(method, new Uri(_options.GetBaseUri(), path));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            string detail = TryReadErrors(errorBody);
            throw new MaxioApiException(response.StatusCode, path, detail);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, path, "Maxio returned an empty response.");
    }

    private static string TryReadErrors(string errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
        {
            return "Maxio returned an error without details.";
        }

        try
        {
            var error = JsonSerializer.Deserialize<MaxioApiError>(errorBody, JsonOptions);
            if (error?.Errors.Count > 0)
            {
                return string.Join("; ", error.Errors);
            }
        }
        catch (JsonException)
        {
            // Do not echo an arbitrary upstream response into our API.
        }

        return "Maxio rejected the request.";
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string path, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Path = path;
    }

    public HttpStatusCode StatusCode { get; }
    public string Path { get; }
}
