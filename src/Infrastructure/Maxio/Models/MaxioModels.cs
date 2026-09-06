using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models for the Maxio Advanced Billing API. Every type and every property below is taken from
// maxio-spec/openapi.yaml; the schema each one mirrors is named above it. Property names are spelled
// out explicitly rather than derived by a naming policy so the mapping to the specification stays
// reviewable. Only the fields this integration reads or sends are modelled — unknown fields in a
// response are ignored, which keeps the client working as Maxio adds fields.

/// <summary>components/schemas/Product-Family.yaml</summary>
public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>components/schemas/Product.yaml</summary>
public class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

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

    [JsonPropertyName("initial_charge_in_cents")]
    public long? InitialChargeInCents { get; set; }

    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; set; }

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    [JsonPropertyName("expiration_interval")]
    public int? ExpirationInterval { get; set; }

    [JsonPropertyName("expiration_interval_unit")]
    public string? ExpirationIntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>
    /// Whether a payment profile has to be on file before a subscription to this product can be
    /// created. Note that the sibling <c>request_credit_card</c> is documented as deprecated and is
    /// not used here.
    /// </summary>
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }

    [JsonPropertyName("product_price_point_id")]
    public long? ProductPricePointId { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>components/schemas/Product-Response.yaml</summary>
public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>components/schemas/Customer.yaml</summary>
public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>components/schemas/Customer-Response.yaml</summary>
public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>components/schemas/Create-Customer.yaml</summary>
public class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

/// <summary>components/schemas/Create-Customer-Request.yaml</summary>
public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

/// <summary>components/schemas/Subscription.yaml</summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>components/schemas/Subscription-State.yaml</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("total_revenue_in_cents")]
    public long TotalRevenueInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("trial_started_at")]
    public DateTimeOffset? TrialStartedAt { get; set; }

    [JsonPropertyName("trial_ended_at")]
    public DateTimeOffset? TrialEndedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; set; }

    /// <summary>components/schemas/Collection-Method.yaml</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>components/schemas/Subscription-Response.yaml</summary>
public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>components/schemas/Create-Subscription.yaml (only the fields this integration sends)</summary>
public class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("payment_collection_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>components/schemas/Create-Subscription-Request.yaml</summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>components/schemas/Site.yaml (only the fields this integration reads)</summary>
public class MaxioSite
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }

    [JsonPropertyName("test")]
    public bool Test { get; set; }
}

/// <summary>components/schemas/Site-Response.yaml</summary>
public class MaxioSiteResponse
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}
