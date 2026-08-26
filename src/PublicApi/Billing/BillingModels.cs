using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The eShopOnWeb user as a billing customer. <paramref name="UserId"/> is the identity
/// from the caller's JWT and becomes the Maxio customer reference.
/// </summary>
public record BillingCustomer(string UserId, string Email, string FirstName, string LastName);

public record BillingPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

public record BillingSubscription(
    int? Id,
    string? Reference,
    string? State,
    string? ProductHandle,
    string? ProductName,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt);

public record SubscribeResult(BillingSubscription Subscription, bool Created);
