namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing system of record.
/// </summary>
/// <param name="Handle">Stable, human readable identifier of the plan. This, never a numeric id, is what callers subscribe to.</param>
/// <param name="Name">Display name of the plan.</param>
/// <param name="Description">Optional marketing description.</param>
/// <param name="PriceInCents">Recurring price of one billing period, in the smallest currency unit.</param>
/// <param name="Interval">Length of a billing period, expressed in <paramref name="IntervalUnit"/>s.</param>
/// <param name="IntervalUnit">Unit of the billing period, for example <c>month</c> or <c>day</c>.</param>
/// <param name="ProductFamilyHandle">Handle of the product family the plan belongs to.</param>
/// <param name="RequiresPaymentMethod">Whether the billing system refuses a signup without a stored payment method.</param>
/// <param name="Taxable">Whether the plan is taxed.</param>
/// <param name="TrialPriceInCents">Price charged for the trial period, when the plan has a trial.</param>
/// <param name="TrialInterval">Length of the trial, expressed in <paramref name="TrialIntervalUnit"/>s, when the plan has a trial.</param>
/// <param name="TrialIntervalUnit">Unit of the trial period, when the plan has a trial.</param>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string? ProductFamilyHandle,
    bool RequiresPaymentMethod,
    bool Taxable,
    long? TrialPriceInCents,
    int? TrialInterval,
    string? TrialIntervalUnit)
{
    /// <summary>Recurring price of one billing period as a major-unit amount (for example 299.00).</summary>
    public decimal Price => decimal.Divide(PriceInCents, 100m);

    /// <summary>Whether the plan starts with a trial period.</summary>
    public bool HasTrial => TrialInterval is > 0;
}
