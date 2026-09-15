using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; init; } = new();
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool PaymentMethodRequired { get; init; }
}

public sealed class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; init; } = new();
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public long CustomerId { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
    public long? CurrentBillingAmountInCents { get; init; }
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioProduct
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; init; } = string.Empty;
    [JsonPropertyName("require_credit_card")]
    public bool? RequireCreditCard { get; init; }
    [JsonPropertyName("request_credit_card")]
    public bool? RequestCreditCard { get; init; }
    public bool Taxable { get; init; }
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; init; } = new();
}

public sealed class MaxioCustomer
{
    public long Id { get; init; }
    public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; init; } = new();
}

public sealed class MaxioSubscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_billing_amount_in_cents")]
    public long? CurrentBillingAmountInCents { get; init; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }
    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public MaxioCustomer? Customer { get; init; }
    public MaxioProduct? Product { get; init; }
}
