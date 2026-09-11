using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("state")]
    public string? State { get; set; }
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }
    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }
    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
    [JsonPropertyName("current_period_started_at")]
    public string? CurrentPeriodStartedAt { get; set; }
    [JsonPropertyName("next_billing_at")]
    public string? NextBillingAt { get; set; }
    [JsonPropertyName("activated_at")]
    public string? ActivatedAt { get; set; }
    [JsonPropertyName("product_family")]
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

public class MaxioProductFamilyRef
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}
