using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Default <see cref="IMaxioApiClient"/> backed by a typed <see cref="HttpClient"/> whose base address and
/// HTTP Basic auth are configured in <see cref="MaxioServiceCollectionExtensions"/>.
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
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

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle,
        CancellationToken cancellationToken = default)
    {
        // The product_family_id path segment accepts a handle prefixed with `handle:` per the spec.
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        var wrappers = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, body: null,
            "list products for product family", cancellationToken);

        var products = new List<MaxioProduct>();
        foreach (var wrapper in wrappers ?? new List<ProductResponse>())
        {
            if (wrapper.Product is not null)
            {
                products.Add(wrapper.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // The lookup endpoint returns 404 with an empty body when no customer matches the reference.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer by reference", cancellationToken);

        var wrapper = await DeserializeAsync<CustomerResponse>(response, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer,
        CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerRequest { Customer = customer };
        var wrapper = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", payload,
            "create customer", cancellationToken);

        if (wrapper?.Customer is null)
        {
            throw new MaxioBillingException("Maxio returned an empty customer on create.");
        }

        return wrapper.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, body: null,
            "list customer subscriptions", cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var wrapper in wrappers ?? new List<SubscriptionResponse>())
        {
            if (wrapper.Subscription is not null)
            {
                subscriptions.Add(wrapper.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription,
        CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionRequest { Subscription = subscription };
        var wrapper = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", payload,
            "create subscription", cancellationToken);

        if (wrapper?.Subscription is null)
        {
            throw new MaxioBillingException("Maxio returned an empty subscription on create.");
        }

        return wrapper.Subscription;
    }

    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? body,
        string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio request failed to {Operation}", operation);
            throw new MaxioBillingException($"Unable to reach Maxio to {operation}.", innerException: ex);
        }

        try
        {
            await EnsureSuccessAsync(response, operation, cancellationToken);
            return await DeserializeAsync<TResponse>(response, cancellationToken);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(content);
        var statusCode = (int)response.StatusCode;

        _logger.LogWarning("Maxio call to {Operation} failed with {StatusCode}: {Body}",
            operation, statusCode, content);

        throw new MaxioBillingException($"Maxio request to {operation} failed.", statusCode, errors);
    }

    private async Task<TResponse?> DeserializeAsync<TResponse>(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Unable to parse Maxio response body: {Body}", content);
            throw new MaxioBillingException("Received an unexpected response from Maxio.", innerException: ex);
        }
    }

    /// <summary>
    /// Extracts human-readable error strings from a Maxio error payload. The spec's error models take several
    /// shapes: <c>{"errors": ["..."]}</c>, <c>{"errors": {"field": "..."}}</c>, or <c>{"errors": "..."}</c>.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string? content)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            AppendErrors(errors, messages);
        }
        catch (JsonException)
        {
            // Not JSON (or malformed) — surface nothing structured; the caller keeps its generic message.
        }

        return messages;
    }

    private static void AppendErrors(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value!);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AppendErrors(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        messages.Add($"{property.Name}: {property.Value.GetString()}");
                    }
                    else
                    {
                        AppendErrors(property.Value, messages);
                    }
                }
                break;
        }
    }
}
