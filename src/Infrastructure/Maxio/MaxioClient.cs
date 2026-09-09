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
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HttpClient implementation of <see cref="IMaxioClient"/> built against the Maxio OpenAPI spec.
/// Authentication (HTTP Basic: API key as username, "x" as password) and the base address are configured
/// on the injected <see cref="HttpClient"/> by <see cref="MaxioServiceCollectionExtensions"/>.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    /// <summary>
    /// Shared serializer options. Maxio uses snake_case field names; the naming policy maps them to our
    /// PascalCase DTO properties in both directions.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient http, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _http = http;
        _logger = logger;

        var options = settings.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new BillingException(
                "Maxio is not configured: 'Maxio:ApiKey' is missing. Provide it via user-secrets or environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new BillingException(
                "Maxio is not configured: set 'Maxio:Subdomain' (to derive the API base URL) or 'Maxio:BaseUrl'.");
        }
    }

    public Task<IReadOnlyList<ProductEnvelope>> ListProductsForFamilyAsync(
        string productFamilyIdOrHandle, CancellationToken cancellationToken = default)
    {
        // The path segment may be "handle:my-family"; escape the value while preserving the "handle:" marker.
        var segment = productFamilyIdOrHandle.StartsWith("handle:", StringComparison.Ordinal)
            ? "handle:" + Uri.EscapeDataString(productFamilyIdOrHandle["handle:".Length..])
            : Uri.EscapeDataString(productFamilyIdOrHandle);

        return GetListAsync<ProductEnvelope>($"product_families/{segment}/products.json", cancellationToken);
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Lookup returns 404 when no customer has that reference — a normal "not found", not an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<CustomerEnvelope>(response, "look up customer by reference", cancellationToken)
            .ConfigureAwait(false);
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<CreateCustomerRequest, CustomerEnvelope>(
            "customers.json", request, "create customer", cancellationToken).ConfigureAwait(false);

        return envelope.Customer
            ?? throw new BillingException("Maxio returned an empty customer when creating a customer.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<CreateSubscriptionRequest, SubscriptionEnvelope>(
            "subscriptions.json", request, "create subscription", cancellationToken).ConfigureAwait(false);

        return envelope.Subscription
            ?? throw new BillingException("Maxio returned an empty subscription when creating a subscription.");
    }

    public Task<IReadOnlyList<SubscriptionEnvelope>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        return GetListAsync<SubscriptionEnvelope>($"customers/{customerId}/subscriptions.json", cancellationToken);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var list = await ReadAsync<List<T>>(response, $"GET {path}", cancellationToken).ConfigureAwait(false);
        return list ?? new List<T>();
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path, TRequest body, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, operation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the response is successful and deserializes its JSON body, translating any failure into a
    /// <see cref="BillingException"/> that carries the upstream status and body for diagnostics.
    /// </summary>
    private async Task<T> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Maxio request failed during '{Operation}': {StatusCode}. Body: {Body}",
                operation, (int)response.StatusCode, payload);

            var detail = ExtractErrorDetail(payload);
            var message = detail is null
                ? $"Maxio request failed while trying to {operation} (HTTP {(int)response.StatusCode})."
                : $"Maxio request failed while trying to {operation} (HTTP {(int)response.StatusCode}): {detail}";

            throw new BillingException(
                message,
                upstreamStatusCode: (int)response.StatusCode,
                upstreamBody: payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new BillingException($"Maxio returned an empty response while trying to {operation}.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            return result ?? throw new BillingException($"Maxio returned an unexpected null body while trying to {operation}.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Maxio response during '{Operation}'. Body: {Body}", operation, payload);
            throw new BillingException($"Could not parse the Maxio response while trying to {operation}.", innerException: ex);
        }
    }

    /// <summary>
    /// Best-effort extraction of a human-readable message from a Maxio error body. Maxio error models
    /// (Error-List-Response, Customer-Error-Response) put messages under "errors" as an array, an object of
    /// field-&gt;message, or a string. Returns null when nothing useful can be extracted.
    /// </summary>
    private static string? ExtractErrorDetail(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            var messages = errors.ValueKind switch
            {
                JsonValueKind.Array => errors.EnumerateArray()
                    .Select(e => e.ToString()),
                JsonValueKind.Object => errors.EnumerateObject()
                    .Select(p => $"{p.Name}: {p.Value}"),
                JsonValueKind.String => new[] { errors.GetString()! },
                _ => Enumerable.Empty<string>(),
            };

            var joined = string.Join("; ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
