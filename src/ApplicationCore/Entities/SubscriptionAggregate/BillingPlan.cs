namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A recurring plan (billing provider "product") available to subscribe to.</summary>
public record BillingPlan(string Handle, string Name, int PriceInCents, string IntervalUnit, int Interval);
