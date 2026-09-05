using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Minimal server-side client for the Maxio Advanced Billing resources used by this application.
/// Numeric identifiers are intentionally never stored: Maxio customer and subscription references
/// are deterministic application identifiers and catalog objects are addressed by API handles.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string planHandle, string subscriptionReference, CancellationToken cancellationToken);
}

public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = $"handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}";
        using var response = await _httpClient.GetAsync($"product_families/{family}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, "list plans", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<MaxioProductEnvelope>();

        return envelopes
            .Select(envelope => envelope.Product)
            .Where(product => product is not null && product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new MaxioPlan(product!.Handle!, product.Name ?? product.Handle!, product.PriceInCents, product.Interval, product.IntervalUnit))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "find customer", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken))?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("customers.json", new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            }
        }, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create customer", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken))?.Customer
            ?? throw new MaxioIntegrationException("Maxio returned an invalid create-customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "find subscription", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken))?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        // Maxio's uniqueness_token protects a timed-out/retried POST. It is deterministic for the
        // logical eShop subscription, so two concurrent clicks use the same duplicate-prevention key.
        var uniquenessToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subscriptionReference))).ToLowerInvariant();
        var request = new
        {
            subscription = new
            {
                customer_reference = customerReference,
                product_handle = planHandle,
                reference = subscriptionReference,
                // The seeded plans allow an invoice/remittance subscription without card capture.
                // "remittance" is the documented collection method for current Relationship Invoicing sites.
                payment_collection_method = "remittance",
                uniqueness_token = uniquenessToken
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        await EnsureSuccessAsync(response, "create subscription", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken))?.Subscription
            ?? throw new MaxioIntegrationException("Maxio returned an invalid create-subscription response.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Maxio error content can contain customer data. Log only the status and operation.
        _logger.LogWarning("Maxio {Operation} failed with HTTP {StatusCode}.", operation, (int)response.StatusCode);
        await response.Content.LoadIntoBufferAsync();
        throw new MaxioIntegrationException($"Maxio could not {operation}. Please try again later.", response.StatusCode);
    }
}

public sealed class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(string message, HttpStatusCode? statusCode = null) : base(message) => StatusCode = statusCode;
    public HttpStatusCode? StatusCode { get; }
}

public sealed record MaxioPlan(string Handle, string Name, long? PriceInCents, int? Interval, string? IntervalUnit);
public sealed record MaxioCustomerInput(string FirstName, string LastName, string Email, string Reference);

public sealed class MaxioCustomerEnvelope { public MaxioCustomer? Customer { get; init; } }
public sealed class MaxioCustomer { public long Id { get; init; } public string? Reference { get; init; } }
public sealed class MaxioProductEnvelope { public MaxioProduct? Product { get; init; } }
public sealed class MaxioProduct
{
    public string? Handle { get; init; }
    public string? Name { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioSubscriptionEnvelope { public MaxioSubscription? Subscription { get; init; } }
public sealed class MaxioSubscription
{
    public long Id { get; init; }
    public string? Reference { get; init; }
    public string? State { get; init; }
    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public MaxioProduct? Product { get; init; }
}
