namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription plan available in the billing system (a Maxio product).
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    string IntervalUnit,
    int Interval);
