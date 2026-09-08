using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A subscription plan (Maxio "product") offered by the configured product family.
/// </summary>
public sealed record SubscriptionPlanInfo(
    string Handle,
    string Name,
    decimal PriceAmount,
    int? Interval,
    string? IntervalUnit);

/// <summary>
/// A subscription owned by the caller, projected for API responses.
/// </summary>
public sealed record SubscriptionInfo(
    int? Id,
    string? PlanHandle,
    string? PlanName,
    decimal? PriceAmount,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate);

/// <summary>
/// Outcome of an idempotent subscribe operation.
/// </summary>
public sealed record SubscribeResult(SubscriptionInfo Subscription, bool Created);

/// <summary>
/// Stable identity/contact used when creating the Maxio customer for an eShopOnWeb user.
/// </summary>
public sealed record CustomerContact(string Email, string FirstName, string LastName);
