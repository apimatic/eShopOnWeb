using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-format DTOs for the Maxio Advanced Billing REST API (https://developers.maxio.com).
// Field names and envelope shapes were verified against the official ab-dotnet-sdk
// (https://github.com/maxio-com/ab-dotnet-sdk) request/response models and controller
// implementations. These types are intentionally internal: PublicApi and the rest of the
// solution only ever see the provider-agnostic MaxioPlan/MaxioSubscription models.

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal sealed class CustomerWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal sealed class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerWire Customer { get; set; } = default!;
}

internal sealed class CreateCustomerWire
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
    public ProductWire? Product { get; set; }
}

internal sealed class ProductWire
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
    public long? PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class SubscriptionWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = default!;

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }

    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionWire Subscription { get; set; } = default!;
}

internal sealed class CreateSubscriptionWire
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = default!;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    /// <summary>
    /// "remittance" tells Maxio not to attempt an automatic card charge on signup, which is
    /// what makes subscribing work for plans configured as payment-method-not-required (no
    /// card on file). See https://docs.maxio.com/hc/en-us/articles/24252287829645.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
