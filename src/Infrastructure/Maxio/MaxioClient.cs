using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed HTTP client over the Maxio (Chargify) Advanced Billing REST API. It owns request
/// construction, JSON (de)serialization and upstream-error translation; higher-level orchestration
/// (idempotency, mapping to domain models) lives in <see cref="MaxioSubscriptionService"/>.
/// Authentication (HTTP Basic) and the base address are configured on the injected HttpClient.
/// </summary>
public class MaxioClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>Looks up a customer by their unique <paramref name="reference"/>. Returns null when none exists.</summary>
    internal async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer", cancellationToken);
        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>Creates a new customer.</summary>
    internal async Task<CustomerDto> CreateCustomerAsync(CustomerAttributesDto attributes, CancellationToken cancellationToken)
    {
        var payload = new CreateCustomerEnvelope { Customer = attributes };
        using var response = await SendAsync(HttpMethod.Post, "customers.json", JsonContent.Create(payload, options: JsonOptions), cancellationToken);
        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new SubscriptionBillingException("Maxio returned an empty customer response.");
        }

        return envelope.Customer;
    }

    /// <summary>Lists the (non-paged first 200) products belonging to the given product family handle.</summary>
    internal async Task<IReadOnlyList<ProductDto>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var url = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);
        await EnsureSuccessAsync(response, "list products for product family", cancellationToken);

        var envelopes = await ReadAsync<List<ProductEnvelope>>(response, cancellationToken) ?? new List<ProductEnvelope>();
        var products = new List<ProductDto>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    /// <summary>
    /// Creates a subscription for an existing customer and product. Payment information is not sent —
    /// the seeded plans do not require a payment method. A duplicate POST with the same
    /// <paramref name="uniquenessToken"/> within 60 minutes is rejected with 409 Conflict.
    /// </summary>
    internal async Task<SubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new SubscriptionCreateDto
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                // Invoice at renewal rather than auto-charging, so signup succeeds without a card on file.
                PaymentCollectionMethod = "remittance",
            },
            UniquenessToken = uniquenessToken,
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", JsonContent.Create(payload, options: JsonOptions), cancellationToken);
        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var envelope = await ReadAsync<SubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new SubscriptionBillingException("Maxio returned an empty subscription response.");
        }

        return envelope.Subscription;
    }

    /// <summary>Lists all subscriptions belonging to a customer.</summary>
    internal async Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await ReadAsync<List<SubscriptionEnvelope>>(response, cancellationToken) ?? new List<SubscriptionEnvelope>();
        var subscriptions = new List<SubscriptionDto>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient surfaces its own timeout as a cancellation not tied to our token.
            throw new SubscriptionBillingException("The Maxio billing service did not respond in time. Please try again.", 504, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException("Unable to reach the Maxio billing service. Please try again later.", 502, ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadBodyAsync(response, cancellationToken);
        var detail = ExtractErrorDetail(body);
        var upstreamStatus = (int)response.StatusCode;

        _logger.LogError("Maxio request to {Operation} failed with status {Status}: {Detail}", operation, upstreamStatus, detail);

        // Map the upstream status to what the API should return to its own caller.
        var (outwardStatus, message) = upstreamStatus switch
        {
            409 => (409, $"This request has already been processed. {detail}".Trim()),
            422 => (422, string.IsNullOrWhiteSpace(detail) ? $"Maxio rejected the request to {operation}." : detail),
            401 or 403 => (502, "The billing service rejected our credentials. Please contact support."),
            429 => (503, "The billing service is busy. Please try again in a moment."),
            _ => (502, $"The billing service failed to {operation}."),
        };

        throw new SubscriptionBillingException(message, outwardStatus);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException("Received an unexpected response from the Maxio billing service.", 502, ex);
        }
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractErrorDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<MaxioErrorEnvelope>(body, JsonOptions);
            if (envelope?.Errors is { Count: > 0 })
            {
                return string.Join("; ", envelope.Errors);
            }
        }
        catch (JsonException)
        {
            // errors may be an object (field -> message) rather than an array; fall through.
        }

        return string.Empty;
    }
}
