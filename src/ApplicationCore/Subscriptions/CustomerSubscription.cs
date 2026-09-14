using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A single subscription enrollment as it exists in Maxio for a given customer.
/// </summary>
public record CustomerSubscription(
    long SubscriptionId,
    string State,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt);
