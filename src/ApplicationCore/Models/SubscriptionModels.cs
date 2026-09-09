using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscribable plan, as exposed by Maxio Advanced Billing.
/// </summary>
public sealed record SubscriptionPlanInfo(
    int MaxioProductId,
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit);

/// <summary>
/// The state of a user's Maxio subscription, confirmed back to the caller.
/// </summary>
public sealed record SubscriptionDetails(
    int MaxioSubscriptionId,
    string MaxioReference,
    int MaxioCustomerId,
    string State,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    decimal Price,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CanceledAt,
    DateTimeOffset CreatedAt);
