using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio;

/// <summary>
/// Thin HTTP client over the subset of the Maxio Advanced Billing API (see maxio-spec/openapi.yaml)
/// needed for subscription enrollment: customers, products (plans) and subscriptions.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Resolves the Maxio API base address per maxio-spec/openapi.yaml's server templating:
    /// "https://{site}.chargify.com", unless an explicit override is configured.
    /// </summary>
    public static Uri ResolveBaseAddress(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return new Uri(options.BaseUrl!.TrimEnd('/') + "/");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain (or Maxio:BaseUrl) must be configured.");
        }

        return new Uri($"https://{options.Subdomain}.chargify.com/");
    }

    /// <summary>GET /customers/lookup.json?reference= - returns null when no customer has that reference.</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    /// <summary>POST /customers.json</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var payload = new MaxioCreateCustomerEnvelope
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await _httpClient.PostAsJsonAsync("customers.json", payload, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    /// <summary>GET /product_families/handle:{handle}/products.json</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return envelopes.Select(e => e.Product).Where(p => p.ArchivedAt is null).ToList();
    }

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return envelopes.Select(e => e.Subscription).ToList();
    }

    /// <summary>POST /subscriptions.json</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        var payload = new MaxioCreateSubscriptionEnvelope
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return result ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned an empty response body." });
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await TryParseErrorsAsync(response, cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    private static async Task<List<string>> TryParseErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return new List<string>();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                var flattened = FlattenErrors(errorsElement);
                if (flattened.Count > 0)
                {
                    return flattened;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through and surface the raw body below.
        }

        return new List<string> { body };
    }

    /// <summary>
    /// Maxio's error shapes vary by endpoint: a plain array of strings (Error-List-Response), or an
    /// object mapping field name to a message or array of messages (Customer-Error-Response). This
    /// flattens either into a single readable list.
    /// </summary>
    private static List<string> FlattenErrors(JsonElement element)
    {
        var result = new List<string>();
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    result.Add(text);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    result.AddRange(FlattenErrors(item));
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in FlattenErrors(property.Value))
                    {
                        result.Add($"{property.Name}: {message}");
                    }
                }
                break;
        }
        return result;
    }
}
