using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed HttpClient over the Maxio Advanced Billing REST API. Each method maps to exactly one
/// operation from the OpenAPI spec. Auth and base address are configured on the injected
/// <see cref="HttpClient"/> during DI registration.
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// GET /product_families/{product_family_id}/products.json (listProductsForProductFamily).
    /// <paramref name="familyHandleOrId"/> may be a numeric id or a handle prefixed with "handle:".
    /// </summary>
    internal async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string familyHandleOrId, CancellationToken cancellationToken)
    {
        var path = $"product_families/{Uri.EscapeDataString(familyHandleOrId)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, "list products for family", cancellationToken)
            ?? new List<MaxioProductEnvelope>();

        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null) products.Add(envelope.Product);
        }

        return products;
    }

    /// <summary>
    /// GET /customers/lookup.json?reference=... (readCustomerByReference). Returns null when no customer
    /// is found for the reference (Maxio replies 404).
    /// </summary>
    internal async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, "lookup customer by reference", cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>POST /customers.json (createCustomer).</summary>
    internal async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(request, options: JsonOptions);
        using var response = await _httpClient.PostAsync("customers.json", content, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, "create customer", cancellationToken);

        return envelope?.Customer
            ?? throw new MaxioException("Maxio returned an empty customer when creating a customer.");
    }

    /// <summary>GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).</summary>
    internal async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, "list customer subscriptions", cancellationToken)
            ?? new List<MaxioSubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null) subscriptions.Add(envelope.Subscription);
        }

        return subscriptions;
    }

    /// <summary>POST /subscriptions.json (createSubscription).</summary>
    internal async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(request, options: JsonOptions);
        using var response = await _httpClient.PostAsync("subscriptions.json", content, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, "create subscription", cancellationToken);

        return envelope?.Subscription
            ?? throw new MaxioException("Maxio returned an empty subscription when creating a subscription.");
    }

    /// <summary>
    /// Reads a successful JSON response into <typeparamref name="T"/>, or throws a
    /// <see cref="MaxioException"/> carrying the upstream status and any error messages on failure.
    /// </summary>
    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errors = ExtractErrors(body);
            var statusCode = (int)response.StatusCode;
            _logger.LogError(
                "Maxio call failed while trying to {Operation}: {StatusCode} {ReasonPhrase}. Errors: {Errors}",
                operation, statusCode, response.ReasonPhrase, string.Join("; ", errors));

            var summary = errors.Count > 0
                ? string.Join("; ", errors)
                : $"{statusCode} {response.ReasonPhrase}";
            throw new MaxioException(
                $"Maxio request to {operation} failed: {summary}", statusCode, errors);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Maxio response for {Operation}.", operation);
            throw new MaxioException($"Could not read Maxio's response when trying to {operation}.", (int)response.StatusCode);
        }
    }

    /// <summary>
    /// Extracts human-readable messages from a Maxio error body. Handles the shapes the spec uses:
    /// a top-level <c>errors</c> array of strings, an <c>errors</c> object mapping fields to strings or
    /// arrays of strings, and a single top-level <c>error</c> string.
    /// </summary>
    private static IReadOnlyList<string> ExtractErrors(string body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("errors", out var errorsElement))
            {
                CollectMessages(errorsElement, messages);
            }

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.String)
            {
                var message = errorElement.GetString();
                if (!string.IsNullOrWhiteSpace(message)) messages.Add(message!);
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                CollectMessages(root, messages);
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. a plain-string 404 body); surface a trimmed snippet.
            var snippet = body.Trim();
            if (snippet.Length > 0) messages.Add(snippet.Length > 500 ? snippet[..500] : snippet);
        }

        return messages;
    }

    private static void CollectMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value)) messages.Add(value!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMessages(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectMessages(property.Value, messages);
                }
                break;
        }
    }
}
