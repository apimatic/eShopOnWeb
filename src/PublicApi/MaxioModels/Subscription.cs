using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.MaxioModels;

public class Subscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_period_starts_at")]
    public DateTime? CurrentPeriodStartsAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    public decimal GetBalanceInDollars() => BalanceInCents / 100m;
}

public class SubscriptionData
{
    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public long? ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "automatic";

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("coupon_codes")]
    public List<string>? CouponCodes { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public SubscriptionData Subscription { get; set; } = new();
}

public class CreateSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public Subscription Subscription { get; set; } = new();
}

public class GetSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public Subscription Subscription { get; set; } = new();
}
