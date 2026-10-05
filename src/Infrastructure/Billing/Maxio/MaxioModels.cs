using System;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

// Model shapes below mirror the Maxio Advanced Billing OpenAPI specification
// (maxio-spec/openapi.yaml and maxio-spec/components/schemas/*.yaml), which is the
// authoritative contract for this integration. Only the fields the subscription
// feature consumes are mapped; the Maxio API returns snake_case, handled by the
// shared JsonOptions below.

/// <summary>Serializer settings shared by all Maxio requests/responses (snake_case, per the API contract).</summary>
public static class MaxioJson
{
    public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Customer object — see components/schemas/Customer.yaml.</summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Wrapper — see components/schemas/Customer-Response.yaml.</summary>
public class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = new();
}

/// <summary>Create Customer request body — see components/schemas/Create-Customer-Request.yaml.</summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCustomerCreate Customer { get; set; } = new();
}

/// <summary>Create Customer object — see components/schemas/Create-Customer.yaml (first_name/last_name/email required).</summary>
public class MaxioCustomerCreate
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Product family object — see components/schemas/Product-Family.yaml.</summary>
public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Wrapper — see components/schemas/Product-Family-Response.yaml.</summary>
public class MaxioProductFamilyResponse
{
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

/// <summary>Product object — see components/schemas/Product.yaml.</summary>
public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Wrapper — see components/schemas/Product-Response.yaml.</summary>
public class MaxioProductResponse
{
    public MaxioProduct Product { get; set; } = new();
}

/// <summary>Subscription object — see components/schemas/Subscription.yaml.</summary>
public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Reference { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>Wrapper — see components/schemas/Subscription-Response.yaml.</summary>
public class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; set; } = new();
}

/// <summary>Create Subscription object — see components/schemas/Create-Subscription.yaml
/// (product_handle/product_id plus customer_id/customer_reference/customer_attributes required).</summary>
public class MaxioSubscriptionCreate
{
    public string? ProductHandle { get; set; }
    public int? ProductId { get; set; }

    /// <summary>Existing Maxio customer id (used when the customer is ensured first).</summary>
    public int? CustomerId { get; set; }

    /// <summary>App-provided reference value for the subscription itself (used for idempotency).</summary>
    public string? Reference { get; set; }

    /// <summary>App-provided reference of an existing Maxio customer (alternative to CustomerId).</summary>
    public string? CustomerReference { get; set; }

    /// <summary>
    /// Collection-Method per the spec ("automatic", "remittance", "prepaid", "invoice").
    /// "remittance" signs up without capturing a payment method (Maxio still bills/records).
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>Create Subscription request body — see components/schemas/Create-Subscription-Request.yaml.</summary>
public class MaxioCreateSubscriptionRequest
{
    public MaxioSubscriptionCreate Subscription { get; set; } = new();
}