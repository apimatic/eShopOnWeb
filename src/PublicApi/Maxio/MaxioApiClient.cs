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
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. All Maxio interactions flow through
/// this client so that authentication, serialization and error handling stay in one place.
/// </summary>
public class MaxioApiClient
{
    private const string PaymentCollectionMethodRemittance = "remittance";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists the products (plans) that belong to the configured product family.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var url = $"/product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        var products = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, url, cancellationToken);
        return products
            .Where(p => p.Product is not null)
            .Select(p => p.Product!)
            .ToList();
    }

    /// <summary>
    /// Reads a single product (plan) by its API handle.
    /// </summary>
    public async Task<MaxioProduct> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var url = $"/products/handle/{Uri.EscapeDataString(handle)}.json";
        var envelope = await SendAsync<MaxioProductEnvelope>(HttpMethod.Get, url, cancellationToken);
        return envelope.Product
            ?? throw new MaxioApiException(HttpStatusCode.NotFound, $"Maxio product '{handle}' was not found.");
    }

    /// <summary>
    /// Looks up a customer by its reference value. Returns null when no customer matches.
    /// </summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, url, cancellationToken);
            return envelope.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a customer. The reference value is unique in Maxio, so a second create for the
    /// same reference is rejected; callers should treat that as "already exists" and re-lookup.
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "/customers.json", cancellationToken, request);
        return envelope.Customer
            ?? throw new MaxioApiException(HttpStatusCode.InternalServerError, "Maxio returned an empty customer response.");
    }

    /// <summary>
    /// Creates a subscription for an existing customer and product. Uses remittance (invoice)
    /// collection so no payment method is required, and a deterministic uniqueness token so a
    /// duplicate request is rejected with 409 instead of creating a second subscription.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        int customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var request = new CreateMaxioSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                PaymentCollectionMethod = PaymentCollectionMethodRemittance,
                UniquenessToken = uniquenessToken
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "/subscriptions.json", cancellationToken, request);
        return envelope.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.InternalServerError, "Maxio returned an empty subscription response.");
    }

    /// <summary>
    /// Lists all subscriptions that belong to a customer.
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var url = $"/customers/{customerId}/subscriptions.json";
        var subscriptions = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, url, cancellationToken);
        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => s.Subscription!)
            .ToList();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, CancellationToken cancellationToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response body.");
        }

        throw await BuildExceptionAsync(response, cancellationToken);
    }

    private async Task<MaxioApiException> BuildExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = $"Maxio API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";
        IReadOnlyList<string> errors = Array.Empty<string>();

        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<MaxioErrorEnvelope>(JsonOptions, cancellationToken);
            if (envelope?.Errors is { Count: > 0 })
            {
                errors = envelope.Errors;
                message = $"Maxio API request failed: {string.Join("; ", envelope.Errors)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse Maxio error response body.");
        }

        return new MaxioApiException(response.StatusCode, message, errors);
    }
}
