using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
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
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

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
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public sealed record CreateMaxioCustomer(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public sealed record CreateMaxioSubscription(
    string ProductHandle,
    string CustomerReference,
    string Reference);

internal sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerBody Customer { get; init; } = new();
}

internal sealed class CreateCustomerBody
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; init; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;
}

internal sealed class CreateSubscriptionRequestBody
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionBody Subscription { get; init; } = new();
}

internal sealed class CreateSubscriptionBody
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; init; } = string.Empty;

    [JsonPropertyName("customer_reference")]
    public string CustomerReference { get; init; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; init; } = "remittance";
}
