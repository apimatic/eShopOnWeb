namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, normalised from the billing provider's catalog.
/// </summary>
/// <param name="Id">The provider assigned identifier. Not stable across a catalog rebuild.</param>
/// <param name="Handle">The stable, human readable identifier used in configuration.</param>
/// <param name="Name">Display name.</param>
/// <param name="Price">Recurring price expressed in major currency units (for example dollars).</param>
/// <param name="Interval">How many <paramref name="IntervalUnit"/>s make up one billing period.</param>
/// <param name="IntervalUnit">The billing period unit, for example <c>month</c>.</param>
/// <param name="RequiresPaymentMethod">Whether the provider demands a payment method at signup.</param>
/// <param name="IsArchived">Whether the plan has been archived and can no longer be subscribed to.</param>
public sealed record BillingPlan(
    int Id,
    string Handle,
    string Name,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod,
    bool IsArchived);
