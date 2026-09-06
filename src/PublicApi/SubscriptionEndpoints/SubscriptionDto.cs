using System;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("nextBillingDate")]
    public DateTime? NextBillingDate { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    public static SubscriptionDto FromMaxioSubscription(MaxioSubscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            ProductName = sub.Product?.Name ?? "Unknown",
            Price = (sub.Product?.PriceInCents ?? 0) / 100m,
            NextBillingDate = string.IsNullOrEmpty(sub.NextAssessmentAt) ? null : DateTime.Parse(sub.NextAssessmentAt),
            CreatedAt = DateTime.Parse(sub.CreatedAt)
        };
    }
}
