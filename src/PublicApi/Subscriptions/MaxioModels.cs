using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public int? PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int? Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("require_credit_card")] public bool? RequireCreditCard { get; set; }
    [JsonPropertyName("request_credit_card")] public bool? RequestCreditCard { get; set; }
    [JsonPropertyName("taxable")] public bool? Taxable { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("customer_id")] public long CustomerId { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product_price_in_cents")] public int? ProductPriceInCents { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProductFamilyResponse
{
    [JsonPropertyName("product_family")] public MaxioProductFamily ProductFamily { get; set; } = new();
}

public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")] public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")] public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")] public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")] public MaxioCreateCustomer Customer { get; set; } = new();
}

public sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_id")] public long CustomerId { get; set; }
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = "remittance";
}
