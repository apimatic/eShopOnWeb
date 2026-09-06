using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

// Wire contracts for the Maxio Advanced Billing API (https://developers.maxio.com/http/advanced-billing-api).
//
// Every property below was verified against the official API surface — the request/response models
// published in Maxio's own SDKs (maxio-com/ab-typescript-sdk, generated from their API spec) — and
// then confirmed against live responses from an Advanced Billing sandbox site. Only the fields this
// integration actually reads are modelled; Maxio adds fields over time and the deserializer ignores
// the rest.
//
// Envelopes: the API wraps every payload in a single-property object keyed by the resource name
// ({"customer": {...}}, {"subscription": {...}}, {"product": {...}}), and list endpoints return an
// array of those envelopes rather than a bare array of resources.

/// <summary>Envelope for a single customer, used by both requests and responses.</summary>
public class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

/// <summary>Body of <c>POST /customers.json</c>.</summary>
public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Unique per site — this is what makes customer creation idempotent.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    /// <summary>Set once the product is archived; archived products are not offered as plans.</summary>
    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }

    /// <summary>
    /// True when Maxio blocks a signup that has no payment method on file. Distinct from
    /// <c>request_credit_card</c>, which only asks the hosted pages to prompt for one.
    /// </summary>
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    /// <summary>Recurring amount actually charged for this subscription, in cents.</summary>
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public string? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio next bills the subscription — the next billing date shown to shoppers.</summary>
    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("trial_ended_at")]
    public string? TrialEndedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public string? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public string? CanceledAt { get; set; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>Body of <c>POST /subscriptions.json</c>.</summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public class MaxioCreateSubscription
{
    /// <summary>API handle of the product to subscribe to. Preferred over the unpublished product id.</summary>
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    /// <summary>Reference of an existing customer to enroll.</summary>
    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    /// <summary>Id of an existing customer to enroll; takes precedence over the reference.</summary>
    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; set; }

    /// <summary>Unique per site — this is what makes subscription creation idempotent.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>
    /// "automatic", "remittance" (Relationship Invoicing) or "invoice" (legacy). Anything other
    /// than "automatic" lets a signup complete without a payment method on file.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

public class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}

public class MaxioSite
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>True on Relationship Invoicing sites, where the non-automatic method is "remittance".</summary>
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }

    /// <summary>True for a sandbox/test site.</summary>
    [JsonPropertyName("test")]
    public bool Test { get; set; }
}

/// <summary>
/// The 422 body. Maxio returns a flat <c>{"errors": ["..."]}</c> for most resources and a
/// per-field map for some customer validations, so both shapes are read leniently.
/// </summary>
public class MaxioErrorEnvelope
{
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}
