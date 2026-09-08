using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/> against the Maxio
/// Advanced Billing API. Authentication is HTTP Basic with the API key as the
/// username and the literal "x" as the password, per the spec's authentication
/// scheme. All request/response shapes come from maxio-spec/openapi.yaml.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const int PageSize = 100;

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var maxioOptions = options.Value;

        var apiKey = maxioOptions.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: Maxio:ApiKey is not set.");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(maxioOptions.ResolveBaseUrl() + "/");
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var batch = await GetJsonAsync<List<MaxioProductEnvelope>>(
                $"products.json?page={page}&per_page={PageSize}", cancellationToken);

            if (batch is null || batch.Count == 0)
            {
                break;
            }

            products.AddRange(batch.Where(e => e.Product is not null).Select(e => e.Product!));

            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var envelope = await GetOrNullOn404Async<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerBody customer, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post, "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer }, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException((int)response.StatusCode, "Response did not contain a customer.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionBody subscription, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post, "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription }, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException((int)response.StatusCode, "Response did not contain a subscription.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var envelope = await GetOrNullOn404Async<MaxioSubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<MaxioSubscription>();
        var page = 1;

        while (true)
        {
            var batch = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={PageSize}", cancellationToken);

            if (batch is null || batch.Count == 0)
            {
                break;
            }

            subscriptions.AddRange(batch.Where(e => e.Subscription is not null).Select(e => e.Subscription!));

            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return subscriptions;
    }

    private async Task<T?> GetOrNullOn404Async<T>(string path, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), _jsonOptions), Encoding.UTF8, "application/json")
        };
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, body);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
    }
}
