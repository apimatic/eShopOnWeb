using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as recorded in Maxio (the billing system of record).
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public int PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>The end of the current billing period — i.e. the next billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Reference { get; set; }
}
