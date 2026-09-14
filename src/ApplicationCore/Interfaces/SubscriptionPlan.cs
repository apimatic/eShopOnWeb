namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(
    string Handle,
    string Name,
    decimal PriceAmount,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);
