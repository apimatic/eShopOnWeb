using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }
    public int? Interval { get; set; }
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
    [JsonPropertyName("product_family")]
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string Currency { get; set; } = string.Empty;
    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// "remittance" lets a subscription be created without a payment method on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Existing Maxio customer id. Mutually exclusive with <see cref="CustomerAttributes"/>.
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// Creates (or races to create) the customer inline with the subscription.
    /// </summary>
    public MaxioCustomerAttributes? CustomerAttributes { get; set; }
}

public class MaxioCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}
