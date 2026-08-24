using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionDetails(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt,
    string? Currency);
