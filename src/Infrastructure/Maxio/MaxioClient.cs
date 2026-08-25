using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API. Endpoints, payload shapes and the
/// Basic-auth scheme (API key as username, "x" as password) follow maxio-spec/openapi.yaml.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxPerPage = 200; // per the spec: per_page over 200 is clamped to 200

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IAppLogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            // GET /products.json?page=N&per_page=200
            var responses = await SendAsync<List<MaxioProductResponse>>(
                HttpMethod.Get, $"products.json?page={page}&per_page={MaxPerPage}", payload: null, cancellationToken);

            var batch = (responses ?? new List<MaxioProductResponse>())
                .Where(r => r.Product is not null)
                .Select(r => r.Product!)
                .ToList();
            products.AddRange(batch);

            if (batch.Count < MaxPerPage)
            {
                return products;
            }
            page++;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        // GET /customers/lookup.json?reference=...
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadSuccessAsync<MaxioCustomerResponse>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        // POST /customers.json
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var envelope = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Create customer returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        // GET /customers/{customer_id}/subscriptions.json
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get, $"customers/{customerId}/subscriptions.json", payload: null, cancellationToken);

        return (responses ?? new List<MaxioSubscriptionResponse>())
            .Where(r => r.Subscription is not null)
            .Select(r => r.Subscription!)
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string paymentCollectionMethod, CancellationToken cancellationToken = default)
    {
        // POST /subscriptions.json
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Create subscription returned an empty subscription payload.");
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativeUrl, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadSuccessAsync<T>(response, cancellationToken);
    }

    private async Task<T?> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Maxio API {StatusCode} response: {Body}", (int)response.StatusCode, body);
        throw new MaxioApiException(response.StatusCode, ExtractErrors(body));
    }

    private static string ExtractErrors(string body)
    {
        // The spec models errors as Error-List-Response ({ "errors": [ "..." ] });
        // some endpoints return an object instead — fall back to the raw body then.
        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorListResponse>(body, JsonOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return string.Join("; ", parsed.Errors);
            }
        }
        catch (JsonException)
        {
        }
        return body;
    }
}
