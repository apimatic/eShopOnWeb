namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A provider-agnostic view of a recurring plan (billing-provider "product") available for subscription.
/// </summary>
public record BillingPlan(
    string Handle,
    int ProductId,
    string Name,
    long? PriceInCents,
    int? IntervalCount,
    string? IntervalUnit,
    bool RequiresPaymentMethod);
