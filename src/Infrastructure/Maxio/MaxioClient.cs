using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level HTTP client for Maxio Advanced Billing. Every request/response shape here is built
/// against maxio-spec/openapi.yaml, which is the authoritative contract:
///
///  - Auth: HTTP Basic where the username is the API key and the password is "x"
///    (spec securitySchemes.BasicAuth).
///  - Server: https://{site}.chargify.com (US) or https://{site}.ebilling.maxio.com (EU),
///    honoring the optional BaseUrl override (spec "servers" templating).
///  - Endpoints used (all documented in the spec):
///      GET  /products.json?page&amp;per_page          (listProducts)
///      GET  /customers/lookup.json?reference        (readCustomerByReference)
///      POST /customers.json                          (createCustomer)
///      POST /subscriptions.json                      (createSubscription)
///      GET  /customers/{id}/subscriptions.json       (listCustomerSubscriptions)
/// </summary>
internal class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(options.Value.ResolveBaseUrl() + "/");
        var credential = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{options.Value.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credential);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// GET /products.json (listProducts) - paged across the whole site; the caller filters down
    /// to the configured product family because the spec offers no family filter on this endpoint.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListAllProductsAsync(int perPage, int maxPages, CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        for (var page = 1; page <= maxPages; page++)
        {
            var response = await GetJsonAsync<List<MaxioProductResponse>>(
                $"products.json?page={page}&per_page={perPage}", cancellationToken);
            if (response is null || response.Count == 0)
            {
                break;
            }
            products.AddRange(response.Select(r => r.Product!)
                .Where(p => p is not null));
            if (response.Count < perPage)
            {
                break;
            }
        }

        return products;
    }

    /// <summary>
    /// GET /customers/lookup.json?reference=... (readCustomerByReference).
    /// Returns null when no customer carries the given reference (HTTP 404).
    /// </summary>
    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(MaxioJson.SerializerOptions, cancellationToken))?.Customer;
    }

    /// <summary>
    /// POST /customers.json (createCustomer). Request body follows CreateCustomerRequest:
    /// {"customer": {first_name, last_name, email, reference}}.
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var response = await _httpClient.PostAsJsonAsync("customers.json", body, MaxioJson.SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(MaxioJson.SerializerOptions, cancellationToken))?.Customer
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Customer response was empty." });
    }

    /// <summary>
    /// POST /subscriptions.json (createSubscription). Request body follows CreateSubscriptionRequest:
    /// {"subscription": {product_handle, customer_id, reference, payment_collection_method}}.
    ///
    /// payment_collection_method follows the spec's Collection-Method.yaml: plans that require a
    /// payment method use "automatic"; cardless plans (require_credit_card=false) use "remittance",
    /// which enrolls the subscription without card capture / 3-DS on Relationship Invoicing sites.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, bool requiresPaymentMethod, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                payment_collection_method = requiresPaymentMethod ? "automatic" : "remittance"
            }
        };

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, MaxioJson.SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(MaxioJson.SerializerOptions, cancellationToken))?.Subscription
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Subscription response was empty." });
    }

    /// <summary>
    /// GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return response?.Select(r => r.Subscription!)
            .Where(s => s is not null)
            .ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<T?> GetJsonAsync<T>(string requestUri, CancellationToken cancellationToken) where T : class
    {
        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(MaxioJson.SerializerOptions, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ParseErrorsAsync(response, cancellationToken);
        _logger.LogWarning("Maxio API call {Uri} failed with HTTP {StatusCode}: {Errors}",
            response.RequestMessage?.RequestUri, (int)response.StatusCode, string.Join("; ", errors));
        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    /// <summary>
    /// Parses the spec's error models. Maxio uses several shapes depending on the endpoint:
    /// {"errors": ["a", "b"]}, {"errors": "single message"}, {"error": "message"}.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ParseErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(MaxioJson.SerializerOptions, cancellationToken);
            if (payload is null)
            {
                return new[] { $"HTTP {(int)response.StatusCode} with an empty body." };
            }

            var messages = new List<string>();
            CollectErrorElement(payload.Errors, messages);
            CollectErrorElement(payload.Error, messages);
            if (messages.Count == 0)
            {
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                messages.Add(string.IsNullOrWhiteSpace(raw)
                    ? $"HTTP {(int)response.StatusCode}."
                    : $"HTTP {(int)response.StatusCode}: {raw}");
            }
            return messages;
        }
        catch (JsonException)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return new[] { $"HTTP {(int)response.StatusCode}: {Truncate(raw)}" };
        }
    }

    private static void CollectErrorElement(JsonElement? element, List<string> messages)
    {
        if (element is null || element.Value.ValueKind == JsonValueKind.Null || element.Value.ValueKind == JsonValueKind.Undefined)
        {
            return;
        }

        switch (element.Value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.Value.EnumerateArray())
                {
                    messages.Add(item.ToString());
                }
                break;
            case JsonValueKind.String:
                messages.Add(element.Value.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.Value.EnumerateObject())
                {
                    messages.Add($"{property.Name}: {property.Value}");
                }
                break;
            default:
                messages.Add(element.Value.ToString());
                break;
        }
    }

    private static string Truncate(string value)
    {
        value = value.Trim();
        return value.Length <= 500 ? value : value[..500];
    }
}
