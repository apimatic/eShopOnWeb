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

public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionRequest request, CancellationToken cancellationToken);
}

public sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        _options.Validate();
        var products = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            using var response = await SendAsync(HttpMethod.Get,
                $"products.json?page={page}&per_page=200", null, cancellationToken);
            var pageProducts = await ReadJsonAsync<List<MaxioProductResponseEnvelope>>(response, cancellationToken)
                ?? new List<MaxioProductResponseEnvelope>();
            products.AddRange(pageProducts.Where(item => item.Product is not null).Select(item => item.Product));
            if (pageProducts.Count < 200)
                return products;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        _options.Validate();
        using var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken,
            allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        var envelope = await ReadJsonAsync<MaxioCustomerResponseEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerRequest request, CancellationToken cancellationToken)
    {
        _options.Validate();
        using var response = await SendAsync(HttpMethod.Post, "customers.json",
            JsonContent.Create(new MaxioCustomerRequestEnvelope { Customer = request }, options: JsonOptions), cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerResponseEnvelope>(response, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        _options.Validate();
        using var response = await SendAsync(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioSubscriptionResponseEnvelope>>(response, cancellationToken)
            ?? new List<MaxioSubscriptionResponseEnvelope>();
        return envelopes.Where(item => item.Subscription is not null).Select(item => item.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionRequest request, CancellationToken cancellationToken)
    {
        _options.Validate();
        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json",
            JsonContent.Create(new MaxioSubscriptionRequestEnvelope { Subscription = request }, options: JsonOptions), cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionResponseEnvelope>(response, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, HttpContent? content,
        CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, relativePath) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x")));
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            return response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;
        response.Dispose();
        throw new MaxioApiException(statusCode, ExtractError(body));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
                return errors.ToString();
        }
        catch (JsonException)
        {
            // Preserve a generic error for non-JSON responses; never expose credentials.
        }

        return "Maxio rejected the request.";
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
