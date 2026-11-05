using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A purchasable recurring plan from the billing system's catalog.</summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    int? TrialInterval,
    string? TrialIntervalUnit,
    long? SetupFeeInCents)
{
    public decimal Price => PriceInCents / 100m;
}

/// <summary>A user's subscription as recorded by the billing system.</summary>
public record UserSubscription(
    string SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt)
{
    public decimal Price => PriceInCents / 100m;
}

/// <param name="Subscription">The subscription the user now holds for the plan.</param>
/// <param name="Created">False when the user already held the plan and the existing subscription was returned.</param>
public record SubscribeResult(UserSubscription Subscription, bool Created);

/// <summary>Identity of the caller as needed to provision a billing customer.</summary>
public record SubscriberIdentity(string UserName, string? Email, string? FirstName, string? LastName);
