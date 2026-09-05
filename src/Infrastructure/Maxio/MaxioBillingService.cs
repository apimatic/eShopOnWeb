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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing. This is the only place in the solution that talks
/// to Maxio; everything else works against <see cref="IMaxioBillingService"/>.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private const string SiteCurrencyCacheKey = "Maxio:SiteCurrency";
    private static readonly TimeSpan SiteCurrencyCacheDuration = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options, IMemoryCache cache, IAppLogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var currency = await GetSiteCurrencyAsync(cancellationToken);
        var path = $"product_families/handle:{_options.ProductFamilyHandle}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(SerializerOptions, cancellationToken)
            ?? new List<ProductEnvelope>();

        return envelopes
            .Where(e => e.Product is not null)
            .Select(e => MapPlan(e.Product!, currency))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null) return existing;

        var payload = new CustomerEnvelope
        {
            Customer = new CustomerJson { Reference = reference, Email = email, FirstName = firstName, LastName = lastName }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, SerializerOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference is unique in Maxio: a 422 here most likely means a concurrent request
            // (e.g. a double-click) already created this customer between our lookup and this
            // create call. Re-check before surfacing the error, so the caller still gets the
            // one true customer instead of a spurious failure.
            var raced = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null) return raced;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        _logger.LogInformation("Created Maxio customer {0} for reference {1}", created?.Customer?.Id ?? 0, reference);
        return MapCustomer(created!.Customer!);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(int maxioCustomerId, CancellationToken cancellationToken = default)
    {
        var currency = await GetSiteCurrencyAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"customers/{maxioCustomerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(SerializerOptions, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!, currency))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int maxioCustomerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var currency = await GetSiteCurrencyAsync(cancellationToken);
        var payload = new SubscriptionCreateEnvelope
        {
            Subscription = new SubscriptionCreatePayload { CustomerId = maxioCustomerId, ProductHandle = productHandle }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(SerializerOptions, cancellationToken);
        _logger.LogInformation("Created Maxio subscription {0} for customer {1} on plan {2}", created?.Subscription?.Id ?? 0, maxioCustomerId, productHandle);
        return MapSubscription(created!.Subscription!, currency);
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCurrencyCacheKey, out string? cached) && cached is not null) return cached;

        using var response = await _httpClient.GetAsync("site.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SiteEnvelope>(SerializerOptions, cancellationToken);
        var currency = envelope?.Site?.Currency;
        if (string.IsNullOrWhiteSpace(currency))
        {
            _logger.LogWarning("Maxio site.json did not return a currency; falling back to USD");
            currency = "USD";
        }

        _cache.Set(SiteCurrencyCacheKey, currency, SiteCurrencyCacheDuration);
        return currency;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = MaxioErrorParser.ExtractMessage(body) ?? $"Maxio API request failed with status {(int)response.StatusCode}.";
        throw new MaxioApiException((int)response.StatusCode, message);
    }

    private static MaxioCustomer MapCustomer(CustomerJson customer) => new(
        customer.Id ?? 0,
        customer.Reference,
        customer.Email ?? string.Empty,
        customer.FirstName ?? string.Empty,
        customer.LastName ?? string.Empty);

    private static MaxioPlan MapPlan(ProductJson product, string currency) => new(
        product.Handle,
        product.Name,
        product.PriceInCents,
        currency,
        product.Interval,
        product.IntervalUnit,
        product.ProductFamily?.Handle ?? string.Empty);

    private static MaxioSubscription MapSubscription(SubscriptionJson subscription, string currency) => new(
        subscription.Id,
        subscription.State,
        subscription.NextAssessmentAt,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        currency,
        subscription.Customer?.Id ?? 0,
        subscription.Customer?.Reference);
}
