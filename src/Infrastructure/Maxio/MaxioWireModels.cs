using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-shape DTOs for the Maxio Advanced Billing REST API (https://{subdomain}.chargify.com).
// Field names and envelope shapes are confirmed against the official Maxio.AdvancedBillingSdk
// source (https://github.com/maxio-com/ab-dotnet-sdk) - Controllers/CustomersController.cs,
// Controllers/SubscriptionsController.cs, Controllers/ProductFamiliesController.cs and the
// corresponding Models/*.cs - rather than guessed.

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerPayload? Customer { get; set; }
}

internal sealed class CustomerPayload
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

internal sealed class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerPayload Customer { get; set; } = default!;
}

internal sealed class CreateCustomerPayload
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = default!;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = default!;

    [JsonPropertyName("email")]
    public string Email { get; set; } = default!;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = default!;
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductPayload? Product { get; set; }
}

internal sealed class ProductPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = default!;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = default!;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool? RequireCreditCard { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionPayload? Subscription { get; set; }
}

internal sealed class SubscriptionPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = default!;

    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("customer")]
    public CustomerPayload? Customer { get; set; }

    [JsonPropertyName("product")]
    public ProductPayload? Product { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionPayload Subscription { get; set; } = default!;
}

internal sealed class CreateSubscriptionPayload
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    /// <summary>
    /// "remittance" tells Maxio the merchant collects payment outside the system, so signup does
    /// not require (and will not attempt to auto-charge) an on-file payment method. Needed because
    /// Maxio's default "automatic" collection tries to charge immediately at signup and rejects the
    /// subscription with a 422 when no card is on file, even for a product with require_credit_card=false.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
