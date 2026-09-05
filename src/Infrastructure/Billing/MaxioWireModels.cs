using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// Wire-format models for the Maxio Advanced Billing REST API (https://developers.maxio.com).
// Every resource is wrapped in an envelope keyed by its resource name (e.g. {"customer": {...}}),
// and list endpoints return an array of such envelopes. Field names/shapes here are taken directly
// from the current Maxio .NET SDK source (maxio-com/ab-dotnet-sdk, v10.0.0) and are internal to this
// project - they must never leak outside Infrastructure.

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal sealed class CustomerWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal sealed class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerWire Customer { get; set; } = new();
}

internal sealed class CreateCustomerWire
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}

internal sealed class ProductWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class SubscriptionWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }

    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionWire Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionWire
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    /// <summary>
    /// "remittance" (Relationship Invoicing sites) or "invoice" (legacy Statements sites) - both bill
    /// without requiring a card on file at signup. The site's own default is "automatic", which requires
    /// a stored payment method, so this must always be set explicitly. See <see cref="SiteWire"/>.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}

internal sealed class SiteEnvelope
{
    [JsonPropertyName("site")]
    public SiteWire? Site { get; set; }
}

internal sealed class SiteWire
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }
}

/// <summary>
/// Maxio validation-error payload, e.g. {"errors": ["Email can't be blank"]} or
/// {"errors": {"email": ["can't be blank"]}}. We only need the messages for surfacing to the caller.
/// </summary>
internal sealed class MaxioErrorEnvelope
{
    [JsonPropertyName("errors")]
    public System.Text.Json.JsonElement Errors { get; set; }
}
