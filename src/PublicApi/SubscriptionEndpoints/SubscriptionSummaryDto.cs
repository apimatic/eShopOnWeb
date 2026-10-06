using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as reflected by the billing system.
/// </summary>
public class SubscriptionSummaryDto
{
    public int SubscriptionId { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public int CustomerId { get; set; }

    public string CustomerReference { get; set; } = string.Empty;
}