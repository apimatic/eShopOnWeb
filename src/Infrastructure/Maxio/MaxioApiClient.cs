using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/> using Basic authentication
/// (API key as username, "x" as password) against the site's API domain.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const int MaxListPages = 25;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }
        if (string.IsNullOrWhiteSpace(opts.Subdomain) && string.IsNullOrWhiteSpace(opts.BaseUrl))
        {
            throw new InvalidOperationException("Maxio:Subdomain (or Maxio:BaseUrl) is not configured.");
        }

        _httpClient.BaseAddress = new Uri(opts.ResolveBaseUrl().TrimEnd('/') + "/");
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        for (var page = 1; page <= MaxListPages; page++)
        {
            var envelopes = await GetJsonAsync<List<MaxioProductEnvelope>>(
                $"products.json?page={page}&per_page=200", cancellationToken);
            if (envelopes is null || envelopes.Count == 0)
            {
                break;
            }
            products.AddRange(envelopes.Select(e => e.Product));
            if (envelopes.Count < 200)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync<MaxioCustomerEnvelope>(
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken,
                notFoundReturnsNull: true) is { } envelope
            ? envelope.Customer
            : null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerPayload customer, CancellationToken cancellationToken = default)
    {
        var envelope = await PostJsonAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json", new MaxioCreateCustomerRequest { Customer = customer }, cancellationToken);
        return envelope.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle, int customerId, string uniquenessToken,
        string? paymentCollectionMethod = null, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                UniquenessToken = uniquenessToken,
                PaymentCollectionMethod = paymentCollectionMethod,
            }
        };
        var envelope = await PostJsonAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json", request, cancellationToken);
        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json?per_page=200", cancellationToken);
        return envelopes?.Select(e => e.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<T?> GetJsonAsync<T>(string relativeUrl, CancellationToken cancellationToken, bool notFoundReturnsNull = false)
    {
        using var response = await SendAsync(HttpMethod.Get, relativeUrl, requestBody: null, cancellationToken, notFoundReturnsNull);
        if (notFoundReturnsNull && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(string relativeUrl, TRequest requestBody, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody);
        using var response = await SendAsync(HttpMethod.Post, relativeUrl, json, cancellationToken);
        return (await DeserializeAsync<TResponse>(response, cancellationToken))!;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string relativeUrl, string? requestBody, CancellationToken cancellationToken, bool notFoundAllowed = false)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (requestBody is not null)
        {
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }
        if (notFoundAllowed && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, body);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
    }
}
