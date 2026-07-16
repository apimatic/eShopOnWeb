namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, resolved live from the billing provider's product catalog.
/// </summary>
public sealed record BillingPlan(
    string Handle,
    string Name,
    long PriceInCents,
    int IntervalCount,
    IntervalUnit IntervalUnit);
