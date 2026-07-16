namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan available for subscription, as resolved live from the billing provider.
/// </summary>
public record BillingPlan(
    string Handle,
    string Name,
    long PriceInCents,
    int IntervalCount,
    string IntervalUnit);
