using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HttpClient for the Maxio Advanced Billing REST API.
/// Auth (Basic: api-key + "x") and base address are configured at registration.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IAppLogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        // Product families can be addressed by handle via the "handle:" prefix.
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json", cancellationToken);
        return (envelopes ?? new List<MaxioProductEnvelope>()).Select(e => e.Product).ToList();
    }

    public async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("customers.json",
            new MaxioCreateCustomerRequest { Customer = customer }, _jsonOptions, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope!.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return (envelopes ?? new List<MaxioSubscriptionEnvelope>()).Select(e => e.Subscription).ToList();
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, _jsonOptions, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope!.Subscription;
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            string message = $"Maxio API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";
            try
            {
                var error = JsonSerializer.Deserialize<MaxioErrorResponse>(body, _jsonOptions);
                if (error?.Errors is { Count: > 0 })
                {
                    message = string.Join("; ", error.Errors);
                }
            }
            catch (JsonException)
            {
                _logger.LogWarning("Maxio error response body was not JSON: {Body}", body);
            }

            _logger.LogWarning("Maxio API error {StatusCode}: {Message}", (int)response.StatusCode, message);
            throw new MaxioApiException(response.StatusCode, message);
        }

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
    }
}
