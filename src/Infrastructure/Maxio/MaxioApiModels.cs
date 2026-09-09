using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models below mirror the component schemas of maxio-spec/openapi.yaml
// (OpenAPI 3.1, Maxio Advanced Billing). Property names use the JSON naming
// defined in the spec via JsonPropertyName attributes.

/// <summary>
/// ./components/schemas/Product-Response.yaml -> { product: {...} }
/// </summary>
public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

/// <summary>
/// ./components/schemas/Product.yaml
/// </summary>
public class MaxioProduct
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
    public string IntervalUnit { get; set; } = "month";

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; set; }

    [JsonPropertyName("archived_at")]
    public System.DateTime? ArchivedAt { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

/// <summary>
/// Product family reference embedded in Product responses.
/// </summary>
public class MaxioProductFamilyRef
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

/// <summary>
/// ./components/schemas/Customer-Response.yaml -> { customer: {...} }
/// </summary>
public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

/// <summary>
/// ./components/schemas/Customer.yaml
/// </summary>
public class MaxioCustomer
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

/// <summary>
/// ./components/schemas/Create-Customer-Request.yaml -> { customer: {...} }
/// (fields per ./components/schemas/Create-Customer.yaml)
/// </summary>
public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public class MaxioCreateCustomer
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

/// <summary>
/// ./components/schemas/Create-Subscription-Request.yaml -> { subscription: {...} }
/// (fields per ./components/schemas/Create-Subscription.yaml)
/// </summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// eShopOnWeb does not capture payment methods for subscriptions, so the
    /// subscription is billed via invoice collection (spec enum value).
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "invoice";
}

/// <summary>
/// ./components/schemas/Subscription-Response.yaml -> { subscription: {...} }
/// </summary>
public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

/// <summary>
/// ./components/schemas/Subscription.yaml (subset used by the integration)
/// </summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public System.DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public System.DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public System.DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public System.DateTime? CanceledAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// Error bodies per ./components/schemas/errors/*.yaml — Maxio returns either
/// { "errors": [...] } / { "errors": { field: [msgs] } } or a bare array.
/// </summary>
public class MaxioErrorList
{
    [JsonPropertyName("errors")]
    public System.Text.Json.JsonElement Errors { get; set; }
}
