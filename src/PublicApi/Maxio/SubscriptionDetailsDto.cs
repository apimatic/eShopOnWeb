using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Confirmation details of a shopper's subscription in the billing system.
/// </summary>
public sealed class SubscriptionDetailsDto
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDateUtc { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
}
