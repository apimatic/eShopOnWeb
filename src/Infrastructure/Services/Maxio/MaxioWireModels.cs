using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Wire-format models for the Maxio Billing API (Chargify-compatible JSON). Property names use
// snake_case to match the API exactly; these are internal to the Maxio client and are never
// exposed outside Infrastructure.

internal class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }
}

internal class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal class MaxioCustomerCreateEnvelope
{
    [JsonPropertyName("customer")]
    public required MaxioCustomerCreate Customer { get; set; }
}

internal class MaxioCustomerCreate
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; set; }

    [JsonPropertyName("email")]
    public required string Email { get; set; }

    [JsonPropertyName("reference")]
    public required string Reference { get; set; }
}

internal class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public System.DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public System.DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioSubscriptionCreateEnvelope
{
    [JsonPropertyName("subscription")]
    public required MaxioSubscriptionCreate Subscription { get; set; }
}

internal class MaxioSubscriptionCreate
{
    [JsonPropertyName("customer_id")]
    public required long CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; set; }

    /// <summary>
    /// Must be a non-"automatic" option so subscribing does not attempt to charge a card - the
    /// seeded plans require no payment method. Statement sites use "invoice"; Relationship
    /// Invoicing sites use "remittance" (see <see cref="MaxioSite.RelationshipInvoicingEnabled"/>).
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public required string PaymentCollectionMethod { get; set; }
}

internal class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}

internal class MaxioSite
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }
}

internal class MaxioErrorEnvelope
{
    [JsonPropertyName("errors")]
    public JsonErrors? Errors { get; set; }
}

/// <summary>
/// Maxio returns validation errors either as a flat string array or as an object keyed by
/// attribute name; this reads either shape without throwing.
/// </summary>
[JsonConverter(typeof(MaxioErrorsJsonConverter))]
internal class JsonErrors
{
    public List<string> Messages { get; } = new();
}
