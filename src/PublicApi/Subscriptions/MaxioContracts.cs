using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record MaxioCustomerInput(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; init; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; init; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;
}

public sealed class MaxioSite
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; init; }

    [JsonPropertyName("test")]
    public bool Test { get; init; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

public interface IMaxioClient
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, string uniquenessToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, string subscriptionReference, string paymentCollectionMethod, string uniquenessToken, CancellationToken cancellationToken);
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; init; } = new();
}

internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; init; } = new();
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; init; } = new();
}

internal sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite Site { get; init; } = new();
}
