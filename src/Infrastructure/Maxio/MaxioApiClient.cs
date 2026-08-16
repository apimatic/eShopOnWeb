using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Default <see cref="IMaxioApiClient"/> implementation. Uses the injected <see cref="HttpClient"/>
/// whose base address and HTTP Basic auth header are configured during DI registration.
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private const string JsonMediaType = "application/json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdentifier, CancellationToken cancellationToken = default)
    {
        // Path accepts either the numeric family id or its handle prefixed with "handle:".
        var path = $"product_families/{productFamilyIdentifier}/products.json";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioProductEnvelope>>(response, cancellationToken);

        var products = new List<MaxioProduct>();
        if (envelopes is not null)
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken, allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "customers.json", Serialize(request), cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty customer response." }, null);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        var envelopes = await ReadJsonAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        if (envelopes is not null)
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Subscription is not null)
                {
                    subscriptions.Add(envelope.Subscription);
                }
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", Serialize(request), cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty subscription response." }, null);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, new Uri(relativePath, UriKind.Relative));
        if (content is not null)
        {
            request.Content = content;
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return response;
        }

        // Non-success: read the body, translate to a typed exception, and dispose the response.
        var body = await SafeReadStringAsync(response, cancellationToken);
        var statusCode = response.StatusCode;
        response.Dispose();

        var errors = ParseErrors(body);
        _logger.LogWarning(
            "Maxio {Method} {Path} failed: {Status} {Errors}",
            method, relativePath, (int)statusCode, string.Join("; ", errors));

        throw new MaxioApiException(statusCode, errors, body);
    }

    private static StringContent Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        return new StringContent(json, Encoding.UTF8, JsonMediaType);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
    }

    private static async Task<string?> SafeReadStringAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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

    /// <summary>
    /// Parses Maxio error bodies. The spec exposes two shapes: an array of strings
    /// (<c>{ "errors": ["..."] }</c>) and a customer error object
    /// (<c>{ "errors": { "field": "message" } }</c>). Both are handled here.
    /// </summary>
    private static IReadOnlyCollection<string> ParseErrors(string? body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errors.EnumerateArray())
                        {
                            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                messages.Add(value!);
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
        }
        catch (JsonException)
        {
            // Not JSON (e.g. a plain-text "A valid product_family_id is required"): use the raw body.
            messages.Add(body.Trim());
        }

        return messages;
    }
}
