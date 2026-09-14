using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioGateway"/> implemented as a typed <see cref="HttpClient"/>. The base address and
/// Basic-auth header are configured once at registration time (see <see cref="MaxioServiceCollectionExtensions"/>).
/// </summary>
internal sealed class MaxioApiClient : IMaxioGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;

    public MaxioApiClient(HttpClient httpClient, IMemoryCache memoryCache)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(productFamilyHandle, cancellationToken);

        using var response = await _httpClient.GetAsync($"product_families/{familyId}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, $"list products for family '{productFamilyHandle}'", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken)
                        ?? new List<ProductEnvelope>();

        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }
        return products;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, "lookup customer by reference", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var payload = new CreateCustomerRequest
        {
            Customer = new CreateCustomerRequest.CustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(response.StatusCode, "create customer", "Response did not contain a customer.");
        }
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken)
                        ?? new List<SubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }
        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionRequest.SubscriptionBody
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                // Invoice ("remittance") collection lets a subscription be created without a stored payment
                // method. Verified against the sandbox: automatic collection returns 422 ("No payment method
                // on file") even for products that do not require a card, because the first period is billed
                // immediately. Remittance invoices the customer instead, yielding an active subscription.
                PaymentCollectionMethod = "remittance"
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(response.StatusCode, "create subscription", "Response did not contain a subscription.");
        }
        return envelope.Subscription;
    }

    /// <summary>
    /// Resolves a product family handle to its numeric id (the products endpoint requires the id).
    /// Cached per handle for the process lifetime window since the mapping is stable within a run.
    /// </summary>
    private async Task<long> ResolveProductFamilyIdAsync(string handle, CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio:product-family-id:{handle}";
        if (_memoryCache.TryGetValue(cacheKey, out long cachedId))
        {
            return cachedId;
        }

        using var response = await _httpClient.GetAsync("product_families.json", cancellationToken);
        await EnsureSuccessAsync(response, "list product families", cancellationToken);

        var families = await response.Content.ReadFromJsonAsync<List<ProductFamilyEnvelope>>(JsonOptions, cancellationToken)
                       ?? new List<ProductFamilyEnvelope>();

        foreach (var envelope in families)
        {
            if (envelope.ProductFamily is not null &&
                string.Equals(envelope.ProductFamily.Handle, handle, StringComparison.OrdinalIgnoreCase))
            {
                var id = envelope.ProductFamily.Id;
                _memoryCache.Set(cacheKey, id, TimeSpan.FromMinutes(30));
                return id;
            }
        }

        throw new MaxioApiException(HttpStatusCode.NotFound, "resolve product family",
            $"No product family found with handle '{handle}'. Check the 'Maxio:ProductFamilyHandle' setting.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            // Body is best-effort diagnostics only.
        }

        throw new MaxioApiException(response.StatusCode, operation, Truncate(body, 1000));
    }

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value!.Length <= maxLength ? value : value.Substring(0, maxLength);
}
