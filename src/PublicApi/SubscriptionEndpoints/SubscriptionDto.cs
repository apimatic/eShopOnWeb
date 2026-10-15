using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string State { get; set; } = string.Empty;

    /// <summary>When the next regularly scheduled charge will be attempted.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails details) => new()
    {
        Id = details.Id,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        PriceInCents = details.PriceInCents,
        Price = details.PriceInCents / 100m,
        Currency = details.Currency,
        State = details.State,
        NextBillingDate = details.NextBillingAt,
        NextAssessmentAt = details.NextAssessmentAt,
        CreatedAt = details.CreatedAt
    };
}
