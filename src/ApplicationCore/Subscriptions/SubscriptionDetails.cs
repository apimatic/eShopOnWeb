using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionDetails(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingAt);
