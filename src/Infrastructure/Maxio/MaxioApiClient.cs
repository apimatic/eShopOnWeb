using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed client over the Maxio Advanced Billing REST API. Handles are used instead of
/// numeric ids wherever the API allows, since ids are reassigned when a site is re-seeded.
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The product family path parameter accepts "handle:{handle}" in place of a numeric id.
        var responses = await GetAsync<List<MaxioProductResponse>>(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json", cancellationToken);

        return (responses ?? new List<MaxioProductResponse>())
            .Where(r => r.Product is not null)
            .Select(r => r.Product!)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var envelope = await PostAsync<CreateCustomerRequest, MaxioCustomerResponse>("customers.json", request, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Create customer succeeded but the response contained no customer.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, string? paymentCollectionMethod = null, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        var envelope = await PostAsync<CreateSubscriptionRequest, MaxioSubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Create subscription succeeded but the response contained no subscription.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var responses = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);

        return (responses ?? new List<MaxioSubscriptionResponse>())
            .Where(r => r.Subscription is not null)
            .Select(r => r.Subscription!)
            .ToList();
    }

    private async Task<T?> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUri, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativeUri, body, JsonOptions, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, errorBody);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }
}
