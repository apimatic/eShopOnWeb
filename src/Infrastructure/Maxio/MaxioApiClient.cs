using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HttpClient implementation of <see cref="IMaxioApiClient"/>. The base address and HTTP Basic
/// authentication are configured on the injected <see cref="HttpClient"/> (see the DI registration).
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        // family_id path param accepts a handle prefixed with "handle:" (per the spec).
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken) ?? new List<MaxioProductEnvelope>();

        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadSuccessAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken)
    {
        var payload = new CreateCustomerEnvelope { Customer = customer };
        using var request = new HttpRequestMessage(HttpMethod.Post, "customers.json")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        using var response = await SendAsync(request, cancellationToken);

        var envelope = await ReadSuccessAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException("Maxio returned an empty customer response when creating a customer.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionEnvelope { Subscription = subscription };
        using var request = new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        using var response = await SendAsync(request, cancellationToken);

        var envelope = await ReadSuccessAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException("Maxio returned an empty subscription response when creating a subscription.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);
        return await ReadSuccessAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning($"Maxio request to {request.Method} {request.RequestUri} failed at the transport layer: {ex.Message}");
            throw new MaxioApiException("Could not reach the Maxio Advanced Billing API.", innerException: ex);
        }
    }

    /// <summary>
    /// Reads a successful JSON body, or converts a non-success response into the appropriate exception:
    /// HTTP 422 → <see cref="MaxioValidationException"/> (correctable bad request), any other non-success
    /// → <see cref="MaxioApiException"/> (upstream failure).
    /// </summary>
    private async Task<T?> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        var statusCode = (int)response.StatusCode;

        _logger.LogWarning(
            $"Maxio request to {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned {statusCode}: {body}");

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var message = errors.Count > 0
                ? string.Join("; ", errors)
                : "Maxio rejected the request.";
            throw new MaxioValidationException(message, errors);
        }

        throw new MaxioApiException(
            $"Maxio returned HTTP {statusCode}.",
            statusCode: statusCode,
            errors: errors);
    }

    /// <summary>
    /// Extracts human readable messages from a Maxio error body. The spec models errors either as an
    /// array of strings (Error-Array-Response) or as an object of field → message (Customer-Error).
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string? body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var element in errors.EnumerateArray())
                    {
                        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            messages.Add(text!);
                        }
                    }
                    break;
                case JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        var value = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.ToString();
                        messages.Add($"{property.Name}: {value}");
                    }
                    break;
                case JsonValueKind.String:
                    var single = errors.GetString();
                    if (!string.IsNullOrWhiteSpace(single))
                    {
                        messages.Add(single!);
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with whatever we have.
        }

        return messages;
    }
}
