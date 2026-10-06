using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A user-facing view of one billing subscription: plan, price, state and next billing date.
/// </summary>
public sealed record SubscriptionSummary(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    string State,
    decimal Price,
    long PriceInCents,
    DateTimeOffset? NextBillingDate);