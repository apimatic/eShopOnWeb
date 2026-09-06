using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;

// Wire-level shapes for the Maxio Advanced Billing REST API. Only the fields this integration
// actually reads are modelled; Maxio returns a great deal more and unmapped members are ignored.
// Maxio wraps most payloads in a single-key envelope ("customer": { ... }), which the *Envelope
// records below mirror.

public sealed record MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; init; }
}

public sealed record MaxioSite
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; init; }

    /// <summary>ISO 4217 code the site bills in, e.g. "USD".</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("test")]
    public bool Test { get; init; }

    /// <summary>
    /// Which subscription architecture the site runs. It decides the valid values for
    /// <c>payment_collection_method</c>: "remittance" on Relationship Invoicing sites,
    /// "invoice" on statement-based ones.
    /// </summary>
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; init; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; init; }
}

public sealed record MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

public sealed record MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }

    /// <summary>"day" or "month".</summary>
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }

    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; init; }

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; init; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; init; }

    [JsonPropertyName("initial_charge_in_cents")]
    public long? InitialChargeInCents { get; init; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; init; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; init; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed record MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }
}

public sealed record MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }
}

public sealed record MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public required MaxioCustomerAttributes Customer { get; init; }
}

public sealed record MaxioCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("reference")]
    public required string Reference { get; init; }
}

public sealed record MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; init; }
}

public sealed record MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>Verbatim Maxio state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    /// <summary>The recurring amount this subscription is actually charged, in cents.</summary>
    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; init; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When payment capture will next be attempted; diverges from the period end after a failure.</summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }

    /// <summary>Null on sites using the catalog-free subscription experience.</summary>
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

public sealed record MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required MaxioCreateSubscriptionAttributes Subscription { get; init; }
}

public sealed record MaxioCreateSubscriptionAttributes
{
    /// <summary>Handles are used rather than ids: Maxio does not publish product ids as stable.</summary>
    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; init; }

    [JsonPropertyName("customer_id")]
    public required long CustomerId { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    /// <summary>
    /// Maxio rejects a second POST carrying the same token within 60 minutes with 409 Conflict,
    /// which makes a timed-out or replayed signup safe to retry.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; init; }

    /// <summary>
    /// How the recurring charge is collected. Left unset it falls back to the site default, which
    /// is usually "automatic" - and an automatic signup fails outright when no card is on file.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; init; }
}
