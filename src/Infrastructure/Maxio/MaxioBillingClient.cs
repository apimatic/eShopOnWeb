using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed client for the Maxio Advanced Billing API. Paths, parameters, request/response
/// shapes and the Basic auth scheme (API key as username, "x" as password) all come from
/// the Maxio OpenAPI specification in maxio-spec/openapi.yaml.
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>GET /product_families/{product_family_id}/products.json — the spec allows "handle:{handle}" as the path id.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        var responses = await GetAsync<List<MaxioProductResponse>>(path, cancellationToken);
        return responses.Select(r => r.Product).Where(p => p is not null).Cast<MaxioProduct>().ToList();
    }

    /// <summary>GET /customers/lookup.json?reference={reference} — single exact match; 404 means "no such customer".</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>POST /customers.json — reference is unique per site, which is what makes signup idempotent.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>("customers.json", request, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Maxio returned an empty customer payload." });
    }

    /// <summary>POST /subscriptions.json — identifies the plan by product_handle and the customer by customer_id (both per spec).</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };

        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.Created, new[] { "Maxio returned an empty subscription payload." });
    }

    /// <summary>GET /customers/{customer_id}/subscriptions.json.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var responses = await GetAsync<List<MaxioSubscriptionResponse>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return responses.Select(r => r.Subscription).Where(s => s is not null).Cast<MaxioSubscription>().ToList();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken)
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty response body." });
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken)
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty response body." });
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Parses the spec's error models: Error-List-Response ({ "errors": [ "..." ] }) and the
    /// customer error variant where "errors" is an object keyed by attribute.
    /// </summary>
    private static async Task<MaxioApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                switch (errorsElement.ValueKind)
                {
                    case JsonValueKind.Array:
                        errors.AddRange(errorsElement.EnumerateArray().Select(e => e.ToString()));
                        break;
                    case JsonValueKind.Object:
                        foreach (var property in errorsElement.EnumerateObject())
                        {
                            errors.Add($"{property.Name}: {property.Value}");
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Body wasn't the documented error shape; fall through to the raw body.
        }

        if (errors.Count == 0)
        {
            errors.Add(string.IsNullOrWhiteSpace(rawBody) ? response.ReasonPhrase ?? "Unknown Maxio error" : rawBody);
        }

        return new MaxioApiException(response.StatusCode, errors, rawBody);
    }

    /// <summary>Configures the shared HttpClient from settings; called once by DI registration.</summary>
    public static void ConfigureHttpClient(HttpClient client, MaxioSettings settings)
    {
        settings.Validate();
        client.BaseAddress = new Uri(settings.GetBaseUrl().TrimEnd('/') + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.Timeout = TimeSpan.FromSeconds(30);
    }
}
