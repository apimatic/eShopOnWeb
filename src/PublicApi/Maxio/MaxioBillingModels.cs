using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A plan the shop offers: a non-archived product of the configured Maxio product family.</summary>
public sealed record SubscriptionPlan(
    int? Id,
    string Handle,
    string? Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool? RequireCreditCard);

/// <summary>
/// The offered plans. <see cref="Truncated"/> is true when the page cap stopped the walk before the provider
/// signalled the end, so the list may be incomplete.
/// </summary>
public sealed record PlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

/// <summary>What the shopper sees of one subscription.</summary>
public sealed record SubscriptionSnapshot(
    int? Id,
    string? Reference,
    string? State,
    string? ProductHandle,
    string? ProductName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcomeKind
{
    /// <summary>This request created the subscription.</summary>
    Created,

    /// <summary>The subscription already existed (an earlier request or a run before a restart created it).</summary>
    Existing,

    /// <summary>The provider may have acted but the outcome could not be confirmed yet; ask again.</summary>
    Pending,
}

public sealed record SubscribeOutcome(SubscribeOutcomeKind Kind, string Reference, SubscriptionSnapshot? Subscription, string Message);

/// <summary>Result of ensuring a Maxio customer exists for an eShop user.</summary>
public sealed record CustomerOutcome(int? MaxioCustomerId)
{
    public bool IsPending => MaxioCustomerId is null;

    public static CustomerOutcome Ready(int id) => new(id);

    public static CustomerOutcome Pending { get; } = new((int?)null);
}

/// <summary>A Maxio customer as the integration reads it.</summary>
public sealed record MaxioCustomerRecord(int Id, string? Reference);
