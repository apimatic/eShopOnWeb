using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

/// <summary>A plan available for subscription, derived from a Maxio product in the configured family.</summary>
public sealed record SubscriptionPlan(
    long ProductId,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequiresCreditCard);

/// <summary>A subscription as seen by an eShopOnWeb shopper.</summary>
public sealed record SubscriptionRecord(
    long SubscriptionId,
    string State,
    string Currency,
    long PriceInCents,
    decimal Price,
    long? ProductId,
    string? ProductHandle,
    string? ProductName,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CanceledAt);

/// <summary>Outcome of an idempotent subscribe operation.</summary>
public sealed record SubscribeResult(bool Created, SubscriptionRecord Subscription);
