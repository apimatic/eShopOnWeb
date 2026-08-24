using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for Maxio Advanced Billing. Every route, wrapper shape and auth scheme
/// here follows the OpenAPI specification in maxio-spec/openapi.yaml:
/// Basic auth (API key as username, "x" as password), ".json" suffixed routes,
/// { customer: ... } / { subscription: ... } request wrappers and *-Response wrappers.
/// </summary>
public class MaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>GET /product_families/{product_family_id}/products.json — the id segment accepts "handle:{handle}".</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var url = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var wrappers = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOptions, cancellationToken);
        return (wrappers ?? new List<MaxioProductResponse>()).Select(w => w.Product).ToList();
    }

    /// <summary>GET /customers/lookup.json?reference=... — returns null when the customer does not exist (404).</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return body?.Customer;
    }

    /// <summary>POST /customers.json — creates a customer; reference must be unique.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return body?.Customer ?? throw new BillingIntegrationException("Maxio returned an empty customer response.", response.StatusCode);
    }

    /// <summary>POST /subscriptions.json — enrolls an existing customer into a product by handle.</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken);
        return body?.Subscription ?? throw new BillingIntegrationException("Maxio returned an empty subscription response.", response.StatusCode);
    }

    /// <summary>GET /customers/{customer_id}/subscriptions.json — all subscriptions belonging to a customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var wrappers = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions, cancellationToken);
        return (wrappers ?? new List<MaxioSubscriptionResponse>()).Select(w => w.Subscription).ToList();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new BillingIntegrationException(
            $"Maxio request failed with status {(int)response.StatusCode} ({response.StatusCode}): {ExtractErrors(body)}",
            response.StatusCode);
    }

    /// <summary>
    /// The spec models errors as either a list of strings or a field-to-message map
    /// (e.g. Error-List-Response / Customer-Error-Response); flatten both to text.
    /// </summary>
    private static string ExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "no response body";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    messages.AddRange(errors.EnumerateArray().Select(e => e.ToString()));
                }
                else if (errors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in errors.EnumerateObject())
                        messages.Add($"{prop.Name}: {prop.Value}");
                }
                if (messages.Count > 0)
                    return string.Join("; ", messages);
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return body.Length <= 500 ? body : body.Substring(0, 500);
    }
}
