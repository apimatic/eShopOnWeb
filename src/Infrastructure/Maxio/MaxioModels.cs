using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A product (plan) as returned by the Maxio Billing API.
/// </summary>
public class MaxioProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Handle { get; set; }

    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTime? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Handle { get; set; }
}

/// <summary>
/// A customer as returned by the Maxio Billing API.
/// </summary>
public class MaxioCustomer
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

/// <summary>
/// A subscription as returned by the Maxio Billing API.
/// </summary>
public class MaxioSubscription
{
    public int Id { get; set; }

    /// <summary>
    /// The subscription state, e.g. active, trialing, past_due, canceled.
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// The recurring amount of the product the subscription is on, in cents.
    /// </summary>
    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    public MaxioCustomerRef? Customer { get; set; }

    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// The customer object embedded in subscription payloads (a subset of customer fields).
/// </summary>
public class MaxioCustomerRef
{
    public int Id { get; set; }

    public string? Reference { get; set; }

    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Envelope used by Maxio list endpoints, which return arrays of
/// <c>{ "product": { ... } }</c> or <c>{ "subscription": { ... } }</c> objects.
/// </summary>
internal class MaxioProductEnvelope
{
    public MaxioProduct Product { get; set; } = new();
}

internal class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}

internal class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new();
}

internal class MaxioSiteEnvelope
{
    public MaxioSite Site { get; set; } = new();
}

internal class MaxioSite
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string DefaultPaymentCollectionMethod { get; set; } = string.Empty;
}

/// <summary>
/// Payload for creating a customer.
/// </summary>
public class MaxioCustomerCreate
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Reference { get; set; }

    public string? Organization { get; set; }
}

/// <summary>
/// The outcome of a subscribe operation.
/// </summary>
public class MaxioSubscribeResult
{
    public MaxioSubscription Subscription { get; set; } = new();

    public MaxioProduct Product { get; set; } = new();

    public MaxioCustomer Customer { get; set; } = new();

    /// <summary>
    /// True when the user was already subscribed to the plan and no new
    /// subscription was created (idempotent re-subscribe).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}