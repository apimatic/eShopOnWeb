namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A plan (billing product) shoppers can subscribe to.</summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    int? TrialInterval,
    string? TrialIntervalUnit,
    bool RequiresPaymentMethod);
