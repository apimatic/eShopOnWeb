namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing system of record.
/// </summary>
/// <param name="Handle">Stable, human readable identifier of the plan. This is the value callers pass when subscribing.</param>
/// <param name="Name">Display name of the plan.</param>
/// <param name="Description">Optional marketing description.</param>
/// <param name="Price">Recurring price per billing period, in <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO 4217 currency code the plan is billed in.</param>
/// <param name="Interval">Number of <paramref name="IntervalUnit"/>s in one billing period.</param>
/// <param name="IntervalUnit">Unit of the billing period, e.g. <c>month</c> or <c>day</c>.</param>
/// <param name="TrialInterval">Length of the free trial, counted in <paramref name="TrialIntervalUnit"/>s, or <c>null</c> when the plan has no trial.</param>
/// <param name="TrialIntervalUnit">Unit the trial length is counted in, e.g. <c>day</c> or <c>month</c>.</param>
/// <param name="RequiresPaymentMethod">Whether the billing system demands a stored payment method at signup.</param>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    int Interval,
    string IntervalUnit,
    int? TrialInterval,
    string? TrialIntervalUnit,
    bool RequiresPaymentMethod);
