using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the Maxio Billing API (Chargify-compatible REST surface) over an HttpClient that is
/// pre-configured (base address + Basic auth) by the host's DI setup. See
/// https://ahshaikh-mintlify-deploy.mintlify.site/introduction/authentication for the auth scheme
/// and the corresponding api-reference pages for each endpoint used below.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    // MaxioApiClient is a typed HttpClient (transient); cache the site's billing architecture in a
    // static field so we only pay for the extra /site.json lookup once per process.
    private static readonly SemaphoreSlim SiteInfoLock = new(1, 1);
    private static bool? _relationshipInvoicingEnabled;

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var wrapper = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return wrapper?.Customer is null ? null : MapCustomer(wrapper.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var body = new CustomerCreateEnvelope
        {
            Customer = new CustomerCreateWire
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        var response = await _httpClient.PostAsJsonAsync("customers.json", body, cancellationToken);
        await EnsureSuccessAsync(response);
        var wrapper = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return MapCustomer(wrapper!.Customer!);
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200", cancellationToken);
        await EnsureSuccessAsync(response);
        var items = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(cancellationToken: cancellationToken) ?? new List<ProductEnvelope>();
        return items
            .Where(i => i.Product is not null && i.Product.ArchivedAt is null)
            .Select(i => MapPlan(i.Product!))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        var body = new SubscriptionCreateEnvelope
        {
            Subscription = new SubscriptionCreateWire
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                // These plans are configured with no payment method required, but the site's default
                // payment_collection_method ("automatic") still demands one on file. Requesting
                // non-automatic collection is what actually makes card-free subscribing work.
                PaymentCollectionMethod = await GetNoCardPaymentCollectionMethodAsync(cancellationToken)
            }
        };

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, cancellationToken);
        await EnsureSuccessAsync(response);
        var wrapper = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(cancellationToken: cancellationToken);
        return MapSubscription(wrapper!.Subscription!);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json?per_page=200", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await EnsureSuccessAsync(response);
        var items = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(cancellationToken: cancellationToken) ?? new List<SubscriptionEnvelope>();
        return items.Where(i => i.Subscription is not null).Select(i => MapSubscription(i.Subscription!)).ToList();
    }

    // Sites with Relationship Invoicing enabled accept "remittance" for non-automatic collection;
    // legacy Statements Architecture sites accept "invoice" instead. See
    // https://ahshaikh-mintlify-deploy.mintlify.site/api-reference/subscriptions/create-subscription
    private async Task<string> GetNoCardPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_relationshipInvoicingEnabled is null)
        {
            await SiteInfoLock.WaitAsync(cancellationToken);
            try
            {
                if (_relationshipInvoicingEnabled is null)
                {
                    var response = await _httpClient.GetAsync("site.json", cancellationToken);
                    await EnsureSuccessAsync(response);
                    var wrapper = await response.Content.ReadFromJsonAsync<SiteEnvelope>(cancellationToken: cancellationToken);
                    _relationshipInvoicingEnabled = wrapper?.Site?.RelationshipInvoicingEnabled ?? false;
                }
            }
            finally
            {
                SiteInfoLock.Release();
            }
        }

        return _relationshipInvoicingEnabled == true ? "remittance" : "invoice";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new MaxioApiException(response.StatusCode, $"Maxio API request to {response.RequestMessage?.RequestUri} failed with {(int)response.StatusCode}: {body}");
    }

    private static MaxioCustomer MapCustomer(CustomerWire wire) => new()
    {
        Id = wire.Id,
        Reference = wire.Reference ?? string.Empty,
        Email = wire.Email ?? string.Empty,
        FirstName = wire.FirstName ?? string.Empty,
        LastName = wire.LastName ?? string.Empty
    };

    private static MaxioPlan MapPlan(ProductWire wire) => new()
    {
        Id = wire.Id,
        Name = wire.Name ?? string.Empty,
        Handle = wire.Handle ?? string.Empty,
        Description = wire.Description,
        PriceInCents = wire.PriceInCents,
        Interval = wire.Interval,
        IntervalUnit = wire.IntervalUnit ?? string.Empty
    };

    private static MaxioSubscription MapSubscription(SubscriptionWire wire) => new()
    {
        Id = wire.Id,
        State = wire.State ?? string.Empty,
        Plan = wire.Product is null ? null : MapPlan(wire.Product),
        CurrentPeriodEndsAt = wire.CurrentPeriodEndsAt,
        CreatedAt = wire.CreatedAt
    };

    // Wire-format DTOs matching Maxio's JSON envelopes. Kept private/internal to this client so the
    // rest of the app only ever sees the framework-agnostic ApplicationCore.Maxio models.
    private class CustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public CustomerWire? Customer { get; set; }
    }

    private class CustomerWire
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }
    }

    private class CustomerCreateEnvelope
    {
        [JsonPropertyName("customer")]
        public CustomerCreateWire Customer { get; set; } = new();
    }

    private class CustomerCreateWire
    {
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;
    }

    private class ProductEnvelope
    {
        [JsonPropertyName("product")]
        public ProductWire? Product { get; set; }
    }

    private class ProductWire
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("price_in_cents")]
        public long PriceInCents { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("interval_unit")]
        public string? IntervalUnit { get; set; }

        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private class SubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public SubscriptionWire? Subscription { get; set; }
    }

    private class SubscriptionCreateEnvelope
    {
        [JsonPropertyName("subscription")]
        public SubscriptionCreateWire Subscription { get; set; } = new();
    }

    private class SubscriptionCreateWire
    {
        [JsonPropertyName("customer_id")]
        public int CustomerId { get; set; }

        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; set; } = string.Empty;

        [JsonPropertyName("payment_collection_method")]
        public string? PaymentCollectionMethod { get; set; }
    }

    private class SiteEnvelope
    {
        [JsonPropertyName("site")]
        public SiteWire? Site { get; set; }
    }

    private class SiteWire
    {
        [JsonPropertyName("relationship_invoicing_enabled")]
        public bool RelationshipInvoicingEnabled { get; set; }
    }

    private class SubscriptionWire
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("product")]
        public ProductWire? Product { get; set; }

        [JsonPropertyName("current_period_ends_at")]
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }
}
