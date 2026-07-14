namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>Period-to-date metered usage balance for a subscription's metered component.</summary>
public record BillingUsageBalance(int SubscriptionId, int RecordedQuantity, int? PeriodToDateUnitBalance);
