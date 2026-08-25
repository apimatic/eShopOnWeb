using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level client for the Maxio Advanced Billing REST API.
/// Endpoint shapes verified against the official Maxio API documentation and the live sandbox:
/// - GET  /product_families/lookup.json?handle={handle}
/// - GET  /product_families/{id}/products.json
/// - GET  /customers/lookup.json?reference={reference}
/// - POST /customers.json
/// - GET  /customers/{id}/subscriptions.json
/// - POST /subscriptions.json
/// Auth is HTTP Basic with the API key as username and any non-empty password ("x" by convention).
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"product_families/lookup.json?handle={Uri.EscapeDataString(handle)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioProductFamilyEnvelope>(response, cancellationToken);
        return envelope?.ProductFamily;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"product_families/{productFamilyId}/products.json", cancellationToken);

        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return envelopes?.Where(e => e.Product != null).Select(e => e.Product!).ToList()
               ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
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

        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
               ?? throw new MaxioApiException(HttpStatusCode.InternalServerError, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/{customerId}/subscriptions.json", cancellationToken);

        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return envelopes?.Where(e => e.Subscription != null).Select(e => e.Subscription!).ToList()
               ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
               ?? throw new MaxioApiException(HttpStatusCode.InternalServerError, "Maxio returned an empty subscription payload.");
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
