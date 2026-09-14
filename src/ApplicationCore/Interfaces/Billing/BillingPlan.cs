namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// A subscribable plan, as published by the billing provider's product catalog.
/// </summary>
public record BillingPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int IntervalCount,
    string IntervalUnit);
