using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

// These transport types intentionally model only fields used by this integration.
// Their wire names and shapes come from maxio-spec/openapi.yaml and its referenced schemas.
public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public required MaxioProduct Product { get; init; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }

    [JsonPropertyName("interval_unit")]
    public required string IntervalUnit { get; init; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; init; }

    [JsonPropertyName("product_family")]
    public required MaxioProductFamily ProductFamily { get; init; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public required MaxioCustomer Customer { get; init; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public required MaxioSubscription Subscription { get; init; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("customer")]
    public required MaxioCustomer Customer { get; init; }

    [JsonPropertyName("product")]
    public required MaxioProduct Product { get; init; }
}

public sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public required MaxioCreateCustomer Customer { get; init; }
}

public sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("reference")]
    public required string Reference { get; init; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required MaxioCreateSubscription Subscription { get; init; }
}

public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; init; }

    [JsonPropertyName("customer_reference")]
    public required string CustomerReference { get; init; }

    [JsonPropertyName("reference")]
    public required string Reference { get; init; }

    [JsonPropertyName("payment_collection_method")]
    public required string PaymentCollectionMethod { get; init; }
}
