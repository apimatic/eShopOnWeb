using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed record SubscriptionPlan(
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    string Currency,
    int Interval,
    string IntervalUnit);

public sealed record UserSubscription(
    long Id,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    string Currency,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscriptionUser(string Id, string Email, string UserName);

public sealed record SubscriptionEnrollment(UserSubscription Subscription, bool Created);
