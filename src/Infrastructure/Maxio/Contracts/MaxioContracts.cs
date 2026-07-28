using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

// Wire contracts mirroring the Maxio Advanced Billing OpenAPI specification (maxio-spec/openapi.yaml).
// Only the fields consumed by this integration are modeled; property names are pinned to the exact
// spec field names via [JsonPropertyName]. Maxio wraps most resources in a single-key envelope
// (e.g. { "customer": { ... } }), which the *Envelope types below represent.

// ---------------------------------------------------------------------------------------------
// Products  (GET /products.json  ->  Product-Response.yaml)
// ---------------------------------------------------------------------------------------------

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProductDto? Product { get; set; }
}

internal sealed class MaxioProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

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

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamilyDto? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamilyDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Customers  (POST/GET /customers.json, GET /customers/lookup.json  ->  Customer-Response.yaml)
// ---------------------------------------------------------------------------------------------

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomerDto? Customer { get; set; }
}

internal sealed class MaxioCustomerDto
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

/// <summary>Request body for <c>POST /customers.json</c> (Create-Customer-Request.yaml).</summary>
internal sealed class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerDto Customer { get; set; } = new();
}

internal sealed class CreateCustomerDto
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

// ---------------------------------------------------------------------------------------------
// Subscriptions  (POST /subscriptions.json, GET /customers/{id}/subscriptions.json
//                 ->  Subscription-Response.yaml)
// ---------------------------------------------------------------------------------------------

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionDto? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public string? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductDto? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomerDto? Customer { get; set; }
}

/// <summary>Request body for <c>POST /subscriptions.json</c> (Create-Subscription-Request.yaml).</summary>
internal sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionDto Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionDto
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    /// <summary>
    /// Collection method (Collection-Method.yaml). <c>remittance</c> bills by invoice with no
    /// automatic charge attempt, which lets subscriptions activate without a stored payment
    /// method (these plans require none / capture no card).
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Site  (GET /site.json  ->  Site-Response.yaml) — used to resolve the site's display currency.
// ---------------------------------------------------------------------------------------------

internal sealed class SiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSiteDto? Site { get; set; }
}

internal sealed class MaxioSiteDto
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Errors  (422 -> Customer-Error-Response.yaml / Error-List-Response.yaml). The `errors` field is
// either an array of strings or an object map of field -> message(s); captured loosely.
// ---------------------------------------------------------------------------------------------

internal sealed class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}
