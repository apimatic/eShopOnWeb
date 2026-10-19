using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record SubscriptionDetails(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    string State,
    decimal? Price,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt);

/// <param name="PendingConfirmation">
/// True when a subscribe request for this user is still unconfirmed by the billing provider.
/// </param>
public record SubscriptionDetailsList(IReadOnlyList<SubscriptionDetails> Subscriptions, bool PendingConfirmation);
