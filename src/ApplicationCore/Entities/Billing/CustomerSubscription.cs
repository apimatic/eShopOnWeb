using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// A shopper's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public sealed class CustomerSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public string? Reference { get; init; }
}
