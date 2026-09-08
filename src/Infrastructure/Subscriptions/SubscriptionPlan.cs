namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed record SubscriptionPlan(string Handle, string Name, decimal Price, string Interval, int IntervalLength);
