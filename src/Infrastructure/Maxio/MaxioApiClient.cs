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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, hand-written client for the subset of the Maxio Advanced Billing API described in
/// maxio-spec/openapi.yaml that eShopOnWeb's subscription flow needs: listing products,
/// finding/creating customers by reference, and finding/creating/listing subscriptions.
/// Every request/response shape here traces back to a path or schema in that spec.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    /// <summary>GET /products.json, filtered to the configured product family (openapi.yaml: listProducts).</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForConfiguredFamilyAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("products.json?per_page=200", cancellationToken);
        var envelopes = await ReadOrThrowAsync<List<MaxioProductEnvelope>>(response, cancellationToken);

        var familyHandle = _options.ProductFamilyHandle;
        return envelopes
            .Select(e => e.Product)
            .Where(p => p.ArchivedAt is null
                && string.Equals(p.ProductFamily.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>GET /customers/lookup.json?reference= (openapi.yaml: readCustomerByReference). Null when not found.</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadOrThrowAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    /// <summary>POST /customers.json (openapi.yaml: createCustomer).</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, JsonOptions, cancellationToken);
        var envelope = await ReadOrThrowAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    /// <summary>GET /subscriptions/lookup.json?reference= (openapi.yaml: findSubscription). Null when not found.</summary>
    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadOrThrowAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription;
    }

    /// <summary>
    /// POST /subscriptions.json (openapi.yaml: createSubscription), for plans configured
    /// with no payment method required. Collection-Method.yaml's non-automatic values are
    /// site-architecture-specific ("remittance" on Relationship Invoicing sites, "invoice"
    /// on legacy Statements Architecture sites); "automatic" is never used here since it
    /// would attempt to charge a card the buyer never supplied. We try "remittance" (the
    /// current architecture) first and fall back to "invoice" so this also works against a
    /// legacy site's catalog.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionWithoutPaymentMethodAsync(string customerReference, string productHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateSubscriptionAsync(customerReference, productHandle, subscriptionReference, "remittance", cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return await CreateSubscriptionAsync(customerReference, productHandle, subscriptionReference, "invoice", cancellationToken);
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, string subscriptionReference, string paymentCollectionMethod, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                CustomerReference = customerReference,
                ProductHandle = productHandle,
                Reference = subscriptionReference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, cancellationToken);
        var envelope = await ReadOrThrowAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription;
    }

    /// <summary>GET /customers/{customer_id}/subscriptions.json (openapi.yaml: listCustomerSubscriptions).</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var envelopes = await ReadOrThrowAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return envelopes.Select(e => e.Subscription).ToList();
    }

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, DescribeError(response.StatusCode, content));
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response body.");
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, $"Failed to parse Maxio response: {ex.Message}");
        }
    }

    private static string DescribeError(HttpStatusCode statusCode, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"Maxio request failed with status {(int)statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    messages.AddRange(errors.EnumerateArray().Select(e => e.ToString()));
                }
                else if (errors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in errors.EnumerateObject())
                    {
                        var fieldMessages = field.Value.ValueKind == JsonValueKind.Array
                            ? string.Join("; ", field.Value.EnumerateArray().Select(e => e.ToString()))
                            : field.Value.ToString();
                        messages.Add($"{field.Name}: {fieldMessages}");
                    }
                }
                else
                {
                    messages.Add(errors.ToString());
                }

                if (messages.Count > 0)
                {
                    return $"Maxio request failed with status {(int)statusCode}: {string.Join(" | ", messages)}";
                }
            }
        }
        catch (JsonException)
        {
            // fall through to raw body below
        }

        return $"Maxio request failed with status {(int)statusCode}: {content}";
    }
}
