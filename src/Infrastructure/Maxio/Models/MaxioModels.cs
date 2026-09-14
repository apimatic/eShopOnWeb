using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models for the Maxio Advanced Billing (Billing API) REST endpoints used by this integration.
// Property names are mapped from snake_case by the serializer options in MaxioJson; only the fields
// the integration actually consumes are modelled, so unrelated schema growth upstream is harmless.

/// <summary>Envelope used by GET /product_families/{id}/products.json list items.</summary>
public class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

/// <summary>Envelope used by the customer endpoints, both request and response.</summary>
public class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>Body of POST /customers.json.</summary>
public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerAttributes Customer { get; set; } = new();

    /// <summary>See <see cref="MaxioCreateSubscriptionRequest.UniquenessToken"/>.</summary>
    [JsonPropertyName("uniqueness_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniquenessToken { get; set; }
}

public class MaxioCreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

/// <summary>Envelope used by the subscription endpoints.</summary>
public class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>Body of POST /subscriptions.json.</summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionAttributes Subscription { get; set; } = new();

    /// <summary>
    /// Maxio's duplicate-prevention token. A second POST carrying the same token within 60 minutes is
    /// rejected with 409, which is what makes retrying a timed-out create safe.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniquenessToken { get; set; }
}

public class MaxioCreateSubscriptionAttributes
{
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// "automatic", "remittance", "invoice" or "prepaid". Omitted when null, which lets the site
    /// default apply.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>Envelope returned by GET /site.json.</summary>
public class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}

public class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? Currency { get; set; }

    /// <summary>
    /// Distinguishes the Relationship Invoicing architecture from the legacy statement-based one.
    /// The two accept different payment_collection_method values.
    /// </summary>
    public bool RelationshipInvoicingEnabled { get; set; }

    public bool Test { get; set; }
}
