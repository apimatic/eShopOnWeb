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
/// Thin, typed HTTP client over the Maxio Advanced Billing REST API. Every route, parameter
/// and payload here is derived from the Maxio OpenAPI contract in <c>maxio-spec/</c>. Base
/// address and Basic authentication are configured on the injected <see cref="HttpClient"/>
/// by the DI registration (see <c>MaxioServiceCollectionExtensions</c>).
/// </summary>
public class MaxioApiClient
{
    /// <summary>Shared serializer options: Maxio uses snake_case JSON.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists the products (plans) in a product family.
    /// <c>GET /product_families/{product_family_id}/products.json</c> — the family may be
    /// referenced by its handle using the <c>handle:</c> prefix.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        await EnsureSuccessAsync(response, $"list products for family '{productFamilyHandle}'", cancellationToken);

        var wrappers = await DeserializeAsync<List<MaxioProductResponse>>(response, cancellationToken) ?? new();
        var products = new List<MaxioProduct>(wrappers.Count);
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Product is not null)
            {
                products.Add(wrapper.Product);
            }
        }
        return products;
    }

    /// <summary>
    /// Finds a customer by your app's reference value.
    /// <c>GET /customers/lookup.json?reference=...</c> — returns <c>null</c> when no match (404).
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, $"look up customer by reference", cancellationToken);
        var wrapper = await DeserializeAsync<MaxioCustomerResponse>(response, cancellationToken);
        return wrapper?.Customer;
    }

    /// <summary>
    /// Creates a customer. <c>POST /customers.json</c>. The <c>reference</c> must be unique;
    /// Maxio rejects a duplicate reference with a 422.
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerRequest { Customer = customer };
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await SendAsync(HttpMethod.Post, "customers.json", content, cancellationToken);
        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var wrapper = await DeserializeAsync<MaxioCustomerResponse>(response, cancellationToken);
        return wrapper?.Customer
            ?? throw new MaxioApiException("Maxio returned an empty body when creating a customer.", (int)response.StatusCode);
    }

    /// <summary>
    /// Lists all subscriptions belonging to a customer.
    /// <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        await EnsureSuccessAsync(response, $"list subscriptions for customer {customerId}", cancellationToken);

        var wrappers = await DeserializeAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken) ?? new();
        var subscriptions = new List<MaxioSubscription>(wrappers.Count);
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Subscription is not null)
            {
                subscriptions.Add(wrapper.Subscription);
            }
        }
        return subscriptions;
    }

    /// <summary>
    /// Creates a subscription. <c>POST /subscriptions.json</c>.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequest { Subscription = subscription };
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", content, cancellationToken);
        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var wrapper = await DeserializeAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return wrapper?.Subscription
            ?? throw new MaxioApiException("Maxio returned an empty body when creating a subscription.", (int)response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            request.Content = content;
        }

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioApiException($"Could not reach the Maxio API ({method} {path}): {ex.Message}", ex);
        }
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException($"Could not parse the Maxio API response: {ex.Message}", ex, (int)response.StatusCode);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadBodyAsync(response, cancellationToken);
        var errors = ParseErrors(body);
        var detail = errors.Count > 0 ? string.Join("; ", errors) : (string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body);

        _logger.LogWarning("Maxio API call failed to {Operation}. Status {StatusCode}. Detail: {Detail}",
            operation, (int)response.StatusCode, detail);

        throw new MaxioApiException(
            $"Maxio API call failed to {operation}. Status {(int)response.StatusCode}. {detail}",
            (int)response.StatusCode,
            errors);
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts human-readable messages from a Maxio error body. The contract models
    /// <c>errors</c> as either an array of strings or an object keyed by field (with string
    /// or array values), so both shapes are handled.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string body)
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
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            messages.Add(item.GetString()!);
                        }
                    }
                    break;

                case JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            messages.Add($"{property.Name}: {property.Value.GetString()}");
                        }
                        else if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in property.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    messages.Add($"{property.Name}: {item.GetString()}");
                                }
                            }
                        }
                    }
                    break;

                case JsonValueKind.String:
                    messages.Add(errors.GetString()!);
                    break;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; leave messages empty and let the caller fall back to the raw body.
        }

        return messages;
    }
}
