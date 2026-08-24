using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing REST API.
/// Endpoints verified against the official API docs (developers.maxio.com) and the live sandbox:
/// <list type="bullet">
/// <item>GET  /product_families/handle:{handle}/products.json — list plans of a family</item>
/// <item>GET  /customers/lookup.json?reference=... — find customer by reference (404 when absent)</item>
/// <item>POST /customers.json — create customer (422 on duplicate reference)</item>
/// <item>POST /subscriptions.json — create subscription (product_handle + customer_reference)</item>
/// <item>GET  /customers/{id}/subscriptions.json — list a customer's subscriptions</item>
/// </list>
/// </summary>
internal class MaxioClient : IMaxioClient
{
    private const string RemittanceCollectionMethod = "remittance";

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var responses = await GetAsync<List<MaxioProductResponse>>(
            $"product_families/handle:{productFamilyHandle}/products.json", cancellationToken);

        return responses
            .Where(r => r.Product is not null)
            .Select(r => r.Product!)
            .Where(p => p.ArchivedAt is null)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={WebUtility.UrlEncode(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
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

        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>("customers.json", request, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = RemittanceCollectionMethod
            }
        };

        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Maxio returned an empty subscription payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var responses = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);

        return responses
            .Where(r => r.Subscription is not null)
            .Select(r => r.Subscription!)
            .ToList();
    }

    private async Task<T> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUri, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativeUri, body, JsonOptions, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, await ReadErrorsAsync(response, cancellationToken));
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return payload ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty response body." });
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var errorResponse = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(JsonOptions, cancellationToken);
            if (errorResponse?.Errors is { Count: > 0 })
            {
                return errorResponse.Errors;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall through to the generic message below.
        }

        return new[] { $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}" };
    }
}
