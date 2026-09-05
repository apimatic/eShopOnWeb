using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>A plan an eShopOnWeb user can subscribe to.</summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

/// <summary>Identifies the eShopOnWeb user (mapped 1:1 to a Maxio customer via <see cref="CustomerReference"/>) subscribing to a plan.</summary>
public record SubscribeToPlanRequest(
    string CustomerReference,
    string Email,
    string FirstName,
    string LastName,
    string PlanHandle);

/// <summary>The confirmed state of a user's enrollment in a plan.</summary>
public record SubscriptionEnrollment(
    long SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    bool AlreadyExisted);
