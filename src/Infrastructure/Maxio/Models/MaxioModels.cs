using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models mirroring the Maxio Advanced Billing (Chargify-compatible) JSON API.
// Property names are verified against the official Maxio .NET SDK docs and the live
// sandbox. Only the fields this integration consumes are modelled; the API returns more.

// ---- Response envelopes (Maxio wraps single/child resources in a named object) ----

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class ProductFamilyEnvelope
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

// ---- Resources ----

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class MaxioProduct
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
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

// ---- Request bodies ----

internal sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerBody Customer { get; set; } = new();
}

internal sealed class CreateCustomerBody
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionBody Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionBody
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

// ---- Error payload: Maxio returns {"errors": ["..."]} (array) on 422, and can also
// return {"errors": {"field": ["..."]}} for some validation shapes. We only need the
// flat list for diagnostics, so the object shape is tolerated and ignored. ----

internal sealed class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}
