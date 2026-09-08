using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// ---------------------------------------------------------------------------
// Request payloads sent to the Maxio Advanced Billing API.
// JSON field names must match the API exactly (snake_case, case-sensitive).
// ---------------------------------------------------------------------------

/// <summary>POST /customers.json body.</summary>
public sealed class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomerCreateRequest Customer { get; set; } = new();
}

/// <summary>Create Customer request fields.</summary>
public sealed class MaxioCustomerCreateRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;
}

/// <summary>POST /subscriptions.json body.</summary>
public sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionCreateRequest Subscription { get; set; } = new();
}

/// <summary>Create Subscription request fields.</summary>
public sealed class MaxioSubscriptionCreateRequest
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    /// <summary>
    /// "remittance" (relationship invoicing) or "invoice" (statements) tells Maxio no card is on file
    /// and to bill by invoice. Required for the cardless subscription flow.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

// ---------------------------------------------------------------------------
// Response DTOs parsed from the Maxio Advanced Billing API.
// Only the fields consumed by the app are modelled; unknown fields are ignored.
// ---------------------------------------------------------------------------

public sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Reference { get; set; }

    public string? Organization { get; set; }
}

public sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }

    public string State { get; set; } = string.Empty;

    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    public MaxioCustomer? Customer { get; set; }

    public MaxioProduct? Product { get; set; }
}

public sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Handle { get; set; }

    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("initial_charge_in_cents")]
    public long? InitialChargeInCents { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Handle { get; set; } = string.Empty;
}

public sealed class SiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite Site { get; set; } = new();
}

public sealed class MaxioSite
{
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }
}

/// <summary>
/// Standard Advanced Billing error body. <c>errors</c> may be a plain string array
/// (e.g. {"errors":["message"]}) or an object keyed by attribute (e.g. {"errors":{"customer":["..."]}}),
/// so it is captured raw and flattened by <see cref="MaxioApiClient"/>.
/// </summary>
public sealed class MaxioErrorEnvelope
{
    public System.Text.Json.JsonElement? Errors { get; set; }
}
