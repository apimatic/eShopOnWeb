using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioClient"/> implemented over a pre-configured <see cref="HttpClient"/> (base address and
/// Basic auth are set up by the DI registration). Serialization uses the snake_case convention the Maxio
/// spec defines for request/response fields.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string familyHandle, CancellationToken cancellationToken = default)
    {
        // The path parameter accepts either the numeric id or the handle prefixed with `handle:`.
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken);

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

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        // Per the spec this lookup returns a single match; a missing reference yields 404.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerBody customer, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest(customer);
        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);

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

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionBody subscription, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest(subscription);
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription;
    }

    /// <summary>Deserializes a success response body, or throws <see cref="MaxioApiException"/> for non-success statuses.</summary>
    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errors = ParseErrors(body);
            _logger.LogWarning("Maxio API {Method} {Uri} returned {StatusCode}: {Errors}",
                response.RequestMessage?.Method, response.RequestMessage?.RequestUri, (int)response.StatusCode,
                errors.Count > 0 ? string.Join("; ", errors) : "(no detail)");
            throw new MaxioApiException(response.StatusCode, errors, body);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new MaxioApiException(response.StatusCode,
                new[] { "Expected a response body but the Maxio API returned none." }, body);
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
            if (result is null)
            {
                throw new MaxioApiException(response.StatusCode,
                    new[] { "The Maxio API response body deserialized to null." }, body);
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode,
                new[] { $"Could not parse the Maxio API response: {ex.Message}" }, body);
        }
    }

    /// <summary>
    /// Extracts human-readable messages from a Maxio error body. The spec's error schemas express
    /// <c>errors</c> as either an array of strings or an object of field:message pairs; both are handled.
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
                    foreach (var item in errors.EnumerateArray())
                    {
                        messages.Add(item.ToString());
                    }
                    break;
                case JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        messages.Add($"{property.Name}: {property.Value}");
                    }
                    break;
                case JsonValueKind.String:
                    messages.Add(errors.GetString() ?? string.Empty);
                    break;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. an HTML gateway page); leave messages empty and rely on status code.
        }

        return messages;
    }
}
