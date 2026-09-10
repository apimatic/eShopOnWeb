using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

// Wire models mirroring the Maxio Advanced Billing OpenAPI schemas. Property names are pinned with
// [JsonPropertyName] to the exact snake_case field names in the spec (the authoritative contract).
// Only the fields the integration reads or writes are modeled; unknown fields are ignored on read.

/// <summary>Envelope for a single product: <c>{ "product": { ... } }</c> (Product-Response).</summary>
internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>Maxio Product (subset of Product.yaml).</summary>
internal sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

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

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Nested product family (subset of Product-Family.yaml).</summary>
internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>Envelope for a single customer: <c>{ "customer": { ... } }</c> (Customer-Response).</summary>
internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Maxio Customer (subset of Customer.yaml / Create-Customer.yaml).</summary>
internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

/// <summary>Create Customer request body: <c>{ "customer": { ... } }</c> (Create-Customer-Request).</summary>
internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomer
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

/// <summary>Envelope for a single subscription: <c>{ "subscription": { ... } }</c> (Subscription-Response).</summary>
internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Maxio Subscription (subset of Subscription.yaml).</summary>
internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_billing_amount_in_cents")]
    public long? CurrentBillingAmountInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Create Subscription request body: <c>{ "subscription": { ... } }</c> (Create-Subscription-Request).</summary>
internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    /// <summary>Set null to leave collection method at the site/product default (omitted from the body).</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>
/// Best-effort holder for Maxio error bodies. Across the spec, validation failures (422) most often
/// carry a top-level <c>errors</c> key whose value is an array of strings; some endpoints use a single
/// <c>error</c> string. Both shapes are captured here.
/// </summary>
internal sealed class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
