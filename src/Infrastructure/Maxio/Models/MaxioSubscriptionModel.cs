using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

public class MaxioSubscriptionModel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomerModel? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductModel? Product { get; set; }
}

public class MaxioSubscriptionItemEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionModel Subscription { get; set; } = new();
}

public class MaxioCreateSubscriptionRequestBody
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionAttributes Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

public class MaxioCreateSubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_reference")]
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>
    /// "invoice" collects the signup balance off-session instead of requiring a stored
    /// payment method, which is what lets signup succeed for plans configured with no
    /// mandatory card capture.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "invoice";
}
