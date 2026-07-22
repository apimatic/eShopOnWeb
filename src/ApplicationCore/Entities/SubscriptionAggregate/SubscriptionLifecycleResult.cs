using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>The outcome of a lifecycle transition, carrying old and new state.</summary>
public sealed record SubscriptionLifecycleResult(
    BillingSubscription Subscription,
    BillingSubscriptionState PreviousState,
    BillingSubscriptionState NewState,
    SubscriptionLifecycleAction Action,
    DateTimeOffset? EffectiveAt,
    string? Message);
