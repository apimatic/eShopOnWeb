using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    int PriceInCents,
    decimal Price,
    string Interval,
    string IntervalUnit,
    string? Currency);

public sealed record SubscriptionDetails(
    int Id,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    decimal Price,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt);

public sealed record SubscriptionEnrollment(SubscriptionDetails Subscription, bool Created);

public sealed record MaxioCustomerInput(string ApplicationUserId, string Email, string FirstName, string LastName);
