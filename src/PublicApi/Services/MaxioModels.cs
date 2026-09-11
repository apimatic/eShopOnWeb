using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSettings
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public class SubscriptionPlan
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";
    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }
    [JsonPropertyName("interval")]
    public int Interval { get; set; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = "";
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class SubscriptionResult
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public string PlanName { get; set; } = "";
    public int PriceInCents { get; set; }
    public string NextBillingDate { get; set; } = "";
    public int CustomerId { get; set; }
}

public class MySubscription
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public string PlanName { get; set; } = "";
    public int PriceInCents { get; set; }
    public string CurrentPeriodEndsAt { get; set; } = "";
    public string NextAssessmentAt { get; set; } = "";
}
