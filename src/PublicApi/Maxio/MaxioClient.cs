using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maxio Advanced Billing REST client. Verified against the Advanced Billing API:
/// Basic auth (API key as username, "x" as password), JSON bodies wrapped per-resource.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The product family template parameter accepts "handle:{handle}" in place of a numeric id.
        var responses = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get, $"product_families/handle:{productFamilyHandle}/products.json", body: null, cancellationToken);

        return responses
            .Where(r => r.Product is not null)
            .Select(r => r.Product!)
            .Where(p => p.ArchivedAt is null)
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
            ?? throw new MaxioApiException(HttpStatusCode.InternalServerError, new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.InternalServerError, new[] { "Maxio returned an empty subscription payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get, $"customers/{customerId}/subscriptions.json", body: null, cancellationToken);

        return responses
            .Where(r => r.Subscription is not null)
            .Select(r => r.Subscription!)
            .ToList();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await ReadAsync<T>(response, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty response body." });
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, await ReadErrorsAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var errors = JsonSerializer.Deserialize<MaxioErrorListResponse>(content, JsonOptions);
            if (errors?.Errors is { Count: > 0 })
            {
                return errors.Errors;
            }

            return new[] { string.IsNullOrWhiteSpace(content) ? response.ReasonPhrase ?? "Unknown Maxio error" : content };
        }
        catch (JsonException)
        {
            return new[] { response.ReasonPhrase ?? "Unknown Maxio error" };
        }
    }
}
