using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Api;

// Wire contracts below mirror the Maxio Advanced Billing OpenAPI specification
// (maxio-spec/openapi.yaml): path/query shapes, request bodies and response schemas.

/// <summary>POST customers.json request body.</summary>
public sealed class CreateCustomerRequestBody
{
    [JsonPropertyName("customer")]
    public CustomerWriteAttributes Customer { get; set; } = new();

    public sealed class CustomerWriteAttributes
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>Unique identifier from the calling app (the eShopOnWeb user).</summary>
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
    }
}

/// <summary>POST subscriptions.json request body.</summary>
public sealed class CreateSubscriptionRequestBody
{
    [JsonPropertyName("subscription")]
    public SubscriptionWriteAttributes Subscription { get; set; } = new();

    public sealed class SubscriptionWriteAttributes
    {
        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; set; } = string.Empty;

        /// <summary>Reference of an existing customer (spec: customer_reference).</summary>
        [JsonPropertyName("customer_reference")]
        public string CustomerReference { get; set; } = string.Empty;

        /// <summary>App-provided unique reference for the subscription itself.</summary>
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        /// <summary>Spec Collection-Method enum: automatic | remittance | prepaid | invoice.</summary>
        [JsonPropertyName("payment_collection_method")]
        public string? PaymentCollectionMethod { get; set; }
    }
}

/// <summary>Envelope for the Customer schema ({"customer": {...}}).</summary>
public sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerData? Customer { get; set; }
}

public sealed class CustomerData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Envelope for the Product schema - a subscription plan ({"product": {...}}).</summary>
public sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductData? Product { get; set; }
}

public sealed class ProductData
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

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

/// <summary>Envelope for the Subscription schema ({"subscription": {...}}).</summary>
public sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionData? Subscription { get; set; }
}

public sealed class SubscriptionData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("customer")]
    public CustomerData? Customer { get; set; }

    [JsonPropertyName("product")]
    public ProductData? Product { get; set; }
}
