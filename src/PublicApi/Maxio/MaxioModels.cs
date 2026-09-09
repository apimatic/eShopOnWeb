using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Minimal projection of a Maxio Advanced Billing product (a subscription plan).
/// Shape verified against the live Advanced Billing API (Chargify-compatible).
/// </summary>
public sealed record MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }

    [JsonPropertyName("trial")]
    public bool? Trial { get; init; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamilyRef? ProductFamily { get; init; }
}

public sealed record MaxioProductFamilyRef
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// Minimal projection of a Maxio Advanced Billing customer.
/// </summary>
public sealed record MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Minimal projection of a Maxio Advanced Billing subscription.
/// </summary>
public sealed record MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("next_billing_at")]
    public DateTime? NextBillingAt { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; init; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; init; }

    [JsonPropertyName("balance_in_cents")]
    public int BalanceInCents { get; init; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; init; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }

    [JsonPropertyName("customer")]
    public MaxioCustomerRef? Customer { get; init; }
}

public sealed record MaxioCustomerRef
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}
