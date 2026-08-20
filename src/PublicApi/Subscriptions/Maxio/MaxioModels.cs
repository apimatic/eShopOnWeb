using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
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
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();

    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

internal sealed class CreateMaxioCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateMaxioCustomer Customer { get; set; } = new();
}

internal sealed class CreateMaxioCustomer
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

internal sealed class CreateMaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateMaxioSubscription Subscription { get; set; } = new();
}

internal sealed class CreateMaxioSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public sealed record MaxioCustomerDetails(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public sealed record MaxioSubscriptionDetails(
    string ProductHandle,
    int CustomerId,
    string Reference);
