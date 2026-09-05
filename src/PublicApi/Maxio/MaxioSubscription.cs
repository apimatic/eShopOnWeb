using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }
    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class SubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public class CreateSubscriptionRequest
{
    public SubscriptionAttributes? Subscription { get; set; }
}

public class SubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
