using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A subscription of an application user in the billing system of record.
/// </summary>
public record SubscriptionInfo
{
    public int SubscriptionId { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public decimal Price { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
    public string? Reference { get; init; }
    public int? CustomerId { get; init; }
}
