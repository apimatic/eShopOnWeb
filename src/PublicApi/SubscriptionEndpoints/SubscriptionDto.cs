using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription owned by the authenticated user, as returned by the API.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    /// <summary>Recurring price in the major currency unit.</summary>
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string? Currency { get; set; }
    public decimal Balance { get; set; }
    /// <summary>The end of the current billing period — the next billing date.</summary>
    public DateTimeOffset? NextBillingDateUtc { get; set; }
    public DateTimeOffset? NextAssessmentAtUtc { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
}