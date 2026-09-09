namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscribable plan as exposed by the billing system of record.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);
